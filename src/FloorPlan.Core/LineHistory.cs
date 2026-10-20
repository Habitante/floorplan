using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FloorPlan.Core;

/// <summary>
/// Lines of the working file: from-to were added or changed, and removed counts the lines taken
/// out there. Only removing lines leaves the empty range [n, n − 1], just before line n.
/// </summary>
public sealed record LineChange(int From, int To, int Removed);

/// <summary>A commit that changed the file, with the lines of the working file its changes became.</summary>
public sealed record CommitLines(string Sha, IReadOnlyList<LineChange> Lines);

/// <summary>Which lines of a file each loaded commit changed, and what is uncommitted, in lines of the working file.</summary>
public sealed record FileLineHistory
{
    /// <summary>Newest first: the commits that changed the file, up to the scan's history limit.</summary>
    public required IReadOnlyList<CommitLines> Commits { get; init; }
    /// <summary>What changed since the last commit; empty when nothing did or the file is not committed.</summary>
    public required IReadOnlyList<LineChange> Working { get; init; }
}

/// <summary>
/// Follows each commit's changes to a file forward through the file's later commits and its
/// uncommitted changes, so a part of the file can be measured as files are: the commits that
/// changed it, and when it last changed. A line rewritten later counts for both commits, at the
/// lines the rewrite left. Like the activity lens, it follows the path: a rename starts anew.
/// </summary>
public static partial class LineHistory
{
    /// <summary>The scan's history limit.</summary>
    public const int MaxCommits = 1000;

    /// <summary>A hunk as boundaries between lines: boundary k lies after line k.</summary>
    internal readonly record struct Hunk(int OldLo, int OldHi, int NewLo, int NewHi);

    [GeneratedRegex(@"^@@ -(?<os>\d+)(?:,(?<oc>\d+))? \+(?<ns>\d+)(?:,(?<nc>\d+))? @@")]
    private static partial Regex HunkHeader();

    public static async Task<FileLineHistory> ForAsync(GitRunner git, string root, string path, CancellationToken cancellationToken)
    {
        var log = await git.RunAsync(root,
            [GitRunner.LiteralPathspecs, "log", "--no-merges", "--no-renames", "--no-ext-diff", "--no-textconv", "-p", "-U0",
             "--format=%x00%H", "-n", MaxCommits.ToString(CultureInfo.InvariantCulture), "--", path],
            cancellationToken);
        var commits = log.ExitCode == 0 ? ParseLog(log.Output) : [];
        var diff = await git.RunAsync(root,
            [GitRunner.LiteralPathspecs, "diff", "--no-renames", "--no-ext-diff", "--no-textconv", "-U0", "HEAD", "--", path],
            cancellationToken);
        var working = diff.ExitCode == 0 ? ParseHunks(Encoding.UTF8.GetString(diff.Output)) : [];
        return Follow(commits, working);
    }

    /// <summary>Each commit's changes carried forward, newest commit first, then the uncommitted ones.</summary>
    internal static FileLineHistory Follow(IReadOnlyList<(string Sha, List<Hunk> Hunks)> commits, List<Hunk> working)
    {
        var result = new List<CommitLines>(commits.Count);
        for (int k = 0; k < commits.Count; k++)
        {
            var lines = new List<LineChange>();
            foreach (var hunk in commits[k].Hunks)
            {
                // The lines this commit's change became, then through each later commit and the working file.
                var (lo, hi) = (hunk.NewLo, hunk.NewHi);
                for (int later = k - 1; later >= 0; later--) (lo, hi) = Map(lo, hi, commits[later].Hunks);
                (lo, hi) = Map(lo, hi, working);
                lines.Add(new LineChange(lo + 1, hi, hunk.OldHi - hunk.OldLo));
            }
            result.Add(new CommitLines(commits[k].Sha, lines));
        }
        return new FileLineHistory
        {
            Commits = result,
            Working = working.Select(h => new LineChange(h.NewLo + 1, h.NewHi, h.OldHi - h.OldLo)).ToList(),
        };
    }

    /// <summary>
    /// Maps a stretch between two boundaries of a file through one change of it. A stretch that
    /// reaches into a replaced region grows to all that replaced it; one that ends where lines
    /// were inserted does not take them in.
    /// </summary>
    static (int Lo, int Hi) Map(int lo, int hi, List<Hunk> hunks)
    {
        int start = MapBoundary(lo, hunks, start: true), end = MapBoundary(hi, hunks, start: false);
        return end < start ? (end, end) : (start, end);
    }

    static int MapBoundary(int p, List<Hunk> hunks, bool start)
    {
        int shift = 0;
        foreach (var h in hunks)
        {
            if (p < h.OldLo) break;
            if (p > h.OldHi)
            {
                shift = h.NewHi - h.OldHi;
                continue;
            }
            // On the hunk or inside it.
            if (start) return p == h.OldHi ? h.NewHi : h.NewLo;
            return p == h.OldLo ? h.NewLo : h.NewHi;
        }
        return p + shift;
    }

    internal static List<(string Sha, List<Hunk> Hunks)> ParseLog(byte[] output)
    {
        var commits = new List<(string, List<Hunk>)>();
        foreach (var record in Encoding.UTF8.GetString(output).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var newline = record.IndexOf('\n');
            var sha = (newline < 0 ? record : record[..newline]).Trim();
            if (sha.Length < 7) continue;
            commits.Add((sha, newline < 0 ? [] : ParseHunks(record[(newline + 1)..])));
        }
        return commits;
    }

    /// <summary>The hunks of a zero-context diff of one file, in order.</summary>
    internal static List<Hunk> ParseHunks(string diff)
    {
        var hunks = new List<Hunk>();
        foreach (var line in diff.Split('\n'))
        {
            if (!line.StartsWith("@@", StringComparison.Ordinal) || HunkHeader().Match(line) is not { Success: true } m) continue;
            int Count(Group g) => g.Success ? int.Parse(g.Value, CultureInfo.InvariantCulture) : 1;
            int os = int.Parse(m.Groups["os"].Value, CultureInfo.InvariantCulture), oc = Count(m.Groups["oc"]);
            int ns = int.Parse(m.Groups["ns"].Value, CultureInfo.InvariantCulture), nc = Count(m.Groups["nc"]);
            // With no lines on a side, Git names the line the change sits after.
            hunks.Add(new Hunk(oc > 0 ? os - 1 : os, oc > 0 ? os - 1 + oc : os, nc > 0 ? ns - 1 : ns, nc > 0 ? ns - 1 + nc : ns));
        }
        return hunks;
    }
}

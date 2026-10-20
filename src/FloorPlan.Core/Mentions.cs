using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FloorPlan.Core;

/// <summary>A file that mentions a name: how many lines do, and the first of them.</summary>
public sealed record MentionFile(string Path, int Count, IReadOnlyList<int> Lines);

/// <summary>Where a name appears in the project's text. Mentions, not calls or references.</summary>
public sealed record MentionReport
{
    public required string Name { get; init; }
    /// <summary>Most mentions first.</summary>
    public required IReadOnlyList<MentionFile> Files { get; init; }
    public int? FilesNotListed { get; init; }
}

/// <summary>
/// Finds the files whose text mentions a name as a whole word, through git grep: tracked files and
/// untracked ones Git does not ignore, text only, read-only. A name can be mentioned without being
/// used, and used without being named (a signal connected by a string built elsewhere), so these
/// are leads, not a call graph.
/// </summary>
public static partial class Mentions
{
    public const int FileLimit = 12;
    public const int LineLimit = 5;

    [GeneratedRegex(@"^[\p{L}_$][\p{L}\p{N}_$]{1,99}$")]
    private static partial Regex Identifier();

    /// <summary>Names worth searching: identifiers of two characters or more.</summary>
    public static bool Searchable(string name) => Identifier().IsMatch(name);

    /// <summary>The files that mention the name, most mentions first, leaving out the one it is declared in.</summary>
    public static async Task<MentionReport?> FindAsync(GitRunner git, string root, string name, string? except, CancellationToken cancellationToken)
    {
        if (!Searchable(name)) return null;
        var counts = await git.RunAsync(root,
            [GitRunner.LiteralPathspecs, "grep", "--untracked", "-I", "-c", "-z", "-w", "-F", "-e", name], cancellationToken);
        // Exit 1 means no file mentions it; anything else, not a repository.
        if (counts.ExitCode == 1) return new MentionReport { Name = name, Files = [] };
        if (counts.ExitCode != 0) return null;

        var files = ParseCounts(counts.Output)
            .Where(f => f.Path != except)
            .OrderByDescending(f => f.Count)
            .ThenBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
        var listed = files.Take(FileLimit).ToList();
        var lines = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        if (listed.Count > 0)
        {
            var found = await git.RunAsync(root,
                [GitRunner.LiteralPathspecs, "grep", "--untracked", "-I", "-n", "-z", "-m", LineLimit.ToString(CultureInfo.InvariantCulture),
                 "-w", "-F", "-e", name, "--", .. listed.Select(f => f.Path)],
                cancellationToken);
            if (found.ExitCode == 0)
                foreach (var (path, line) in ParseLines(found.Output))
                {
                    if (!lines.TryGetValue(path, out var list)) lines[path] = list = [];
                    list.Add(line);
                }
        }
        return new MentionReport
        {
            Name = name,
            Files = listed.Select(f => new MentionFile(f.Path, f.Count, lines.GetValueOrDefault(f.Path) ?? [])).ToList(),
            FilesNotListed = files.Count > FileLimit ? files.Count - FileLimit : null,
        };
    }

    /// <summary>"path\0count" lines.</summary>
    static IEnumerable<(string Path, int Count)> ParseCounts(byte[] output)
    {
        foreach (var record in Encoding.UTF8.GetString(output).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var nul = record.IndexOf('\0');
            if (nul > 0 && int.TryParse(record.AsSpan(nul + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                yield return (record[..nul], count);
        }
    }

    /// <summary>"path\0line\0text" lines.</summary>
    static IEnumerable<(string Path, int Line)> ParseLines(byte[] output)
    {
        foreach (var record in Encoding.UTF8.GetString(output).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var first = record.IndexOf('\0');
            var second = first < 0 ? -1 : record.IndexOf('\0', first + 1);
            if (second > first && int.TryParse(record.AsSpan(first + 1, second - first - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var line))
                yield return (record[..first], line);
        }
    }
}

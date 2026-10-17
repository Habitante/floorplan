using System.Text.RegularExpressions;

namespace FloorPlan.Core;

/// <summary>The HEAD the human last saw on a project, and when they were last there.</summary>
public sealed record SeenMark(string Head, DateTimeOffset At);

/// <summary>A name and the number of commits it covers.</summary>
public sealed record Tally(string Name, int Commits);

/// <summary>
/// What landed while the human was away: the commits from the HEAD they last saw to the scanned
/// one, the regions those touched and who made them. Commits count as <c>git log seen..HEAD</c>
/// counts them, without merges and, for a folder inside a repository, only those touching it.
/// </summary>
public sealed record SinceReport
{
    /// <summary>The HEAD the human last saw.</summary>
    public required string SeenHead { get; init; }
    /// <summary>When the human was last there.</summary>
    public required DateTimeOffset SeenAt { get; init; }
    public int Commits { get; init; }
    /// <summary>The oldest of them in the loaded history, where the history range starts.</summary>
    public string? From { get; init; }
    /// <summary>Some are older than the loaded history, so regions and names cover only the loaded ones.</summary>
    public bool? BeyondHistory { get; init; }
    /// <summary>Why nothing is counted: the commit last seen is not in this history.</summary>
    public string? Problem { get; init; }
    /// <summary>The top-level folders of the map that the commits touched; "" stands for the files at the top level.</summary>
    public IReadOnlyList<Tally>? Regions { get; init; }
    public IReadOnlyList<Tally>? Authors { get; init; }
    /// <summary>Names in co-author lines, where agents committing under their human's name sign.</summary>
    public IReadOnlyList<Tally>? CoAuthors { get; init; }
    /// <summary>When the window showed it: on opening the project, or when the human came back.</summary>
    public DateTimeOffset? ShownAt { get; init; }
    public long? SecondsAgo { get; init; }
}

/// <summary>The commits since a seen HEAD, as Git lists them.</summary>
/// <param name="Shas">Newest first, at most the scan's commit limit.</param>
/// <param name="Count">All of them, which may be more than <paramref name="Shas"/>.</param>
/// <param name="Lost">The seen commit is not an ancestor of HEAD, or no longer exists.</param>
public sealed record Unseen(IReadOnlyList<string> Shas, int Count, bool Lost);

public static partial class Since
{
    /// <summary>Away this long, the human comes back to a briefing on what landed meanwhile.</summary>
    public static readonly TimeSpan ReturnAfter = TimeSpan.FromMinutes(5);

    public const string LostProblem = "The commit last seen is not in this history: rebased, reset or on another branch";

    [GeneratedRegex("^[0-9a-fA-F]{40}$")]
    private static partial Regex Sha();

    /// <summary>What landed in a scanned project since the human saw it; null when nothing did.</summary>
    public static async Task<SinceReport?> ComputeAsync(GitRunner git, ProjectIndex index, SeenMark? seen, CancellationToken cancellationToken)
    {
        var project = index.Scan.Project;
        if (seen is null || !project.Git || project.Head is not { } head || !Sha().IsMatch(seen.Head)) return null;
        if (string.Equals(seen.Head, head, StringComparison.OrdinalIgnoreCase)) return null;
        return Report(index, seen, await ReadAsync(git, project.Root, seen.Head, head, project.CommitLimit, cancellationToken));
    }

    /// <summary>Lists the commits in seen..head as the scan reads history: no merges, and only those touching a subfolder.</summary>
    static async Task<Unseen> ReadAsync(GitRunner git, string root, string seen, string head, int limit, CancellationToken cancellationToken)
    {
        List<string> range = ["--no-merges", $"{seen}..{head}"];
        Task<GitResult> List(IReadOnlyList<string> commits) =>
            git.RunAsync(root, [GitRunner.LiteralPathspecs, "rev-list", $"--max-count={limit}", .. commits], cancellationToken);

        // One round of Git calls when the folder is a repository's top, as it usually is.
        var ancestor = git.RunAsync(root, ["merge-base", "--is-ancestor", seen, head], cancellationToken);
        var prefix = git.RunCheckedAsync(root, ["rev-parse", "--show-prefix"], cancellationToken);
        var whole = List(range);
        await Task.WhenAll(ancestor, prefix, whole);
        // 1: not an ancestor; anything else: the commit is gone.
        if (ancestor.Result.ExitCode != 0) return new Unseen([], 0, Lost: true);

        // From a subfolder, only the commits that touch it, as the scan reads history.
        if (prefix.Result.Text.Length > 0) range.AddRange(["--", "."]);
        var listed = prefix.Result.Text.Length > 0 ? await List(range) : whole.Result;
        if (listed.ExitCode != 0) throw new GitException($"git rev-list failed ({listed.ExitCode}): {listed.Error}");
        var shas = listed.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        int count = shas.Length;
        if (count >= limit)
        {
            var counted = await git.RunCheckedAsync(root, [GitRunner.LiteralPathspecs, "rev-list", "--count", .. range], cancellationToken);
            if (int.TryParse(counted.Text, out int all)) count = all;
        }
        return new Unseen(shas, count, Lost: false);
    }

    /// <summary>The briefing for commits Git listed, described from the scan's history; null when there are none.</summary>
    public static SinceReport? Report(ProjectIndex index, SeenMark seen, Unseen unseen)
    {
        var mark = new SinceReport { SeenHead = BoardReports.Short(seen.Head), SeenAt = seen.At };
        if (unseen.Lost) return mark with { Problem = LostProblem };
        if (unseen.Count == 0) return null;

        // The scan lists commits newest first, so the oldest unseen one has the highest position.
        var commits = index.Scan.Commits;
        var position = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < commits.Count; i++) position.TryAdd(commits[i].Sha, i);
        var loaded = unseen.Shas.Where(position.ContainsKey).Select(sha => position[sha]).Order().Select(i => commits[i]).ToList();

        // Files at the top level count as one region, so they do not crowd out the folders.
        string? Region(int id)
        {
            if (index.File(id)?.Path is not { } path) return null;
            int slash = path.IndexOf('/');
            return slash > 0 ? path[..slash] : "";
        }
        return mark with
        {
            Commits = unseen.Count,
            From = loaded.Count > 0 ? BoardReports.Short(loaded[^1].Sha) : null,
            BeyondHistory = loaded.Count < unseen.Count ? true : null,
            Regions = Tallies(loaded.SelectMany(c => c.Files.Select(Region).OfType<string>().Distinct())),
            Authors = Tallies(loaded.Select(c => c.Author)),
            CoAuthors = Tallies(loaded.SelectMany(c => c.CoAuthors ?? [])),
        };
    }

    /// <summary>Each name with its count, most first; null when there are none.</summary>
    static List<Tally>? Tallies(IEnumerable<string> names)
    {
        var tallies = names
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Tally(g.First(), g.Count()))
            .OrderByDescending(t => t.Commits)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
        return tallies.Count > 0 ? tallies : null;
    }
}

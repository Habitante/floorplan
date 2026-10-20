using System.Text.Json;
using System.Text.RegularExpressions;

namespace FloorPlan.Core;

// Meaning on paths: a project description that an agent writes and FloorPlan checks. It names the
// project's systems by path anchors, its direction and its findings, each with its basis and the
// documents behind it. It lives outside the inspected project. Anchors are checked against every
// scan: one that names nothing is reported as stale, never dropped.

/// <summary>The description as the agent wrote it (format version 1).</summary>
public sealed record ProjectDescription
{
    /// <summary>The format version.</summary>
    public int Floorplan { get; init; }
    public string? Project { get; init; }
    public DescriptionSource? Written { get; init; }
    public string? Summary { get; init; }
    public IReadOnlyList<SystemEntry> Systems { get; init; } = [];
    public IReadOnlyList<DirectionEntry> Direction { get; init; } = [];
    public IReadOnlyList<FindingEntry> Findings { get; init; } = [];
}

/// <summary>Who wrote the description, when, at which HEAD, and from which documents.</summary>
public sealed record DescriptionSource
{
    public string? At { get; init; }
    public string? By { get; init; }
    public string? Head { get; init; }
    public IReadOnlyList<string>? From { get; init; }
}

/// <summary>
/// A part of the project: where it lives (path anchors: a file, a folder, or a glob with * and **)
/// and what it is for. Basis: observed (from the files), declared (from the project's documents)
/// or inferred (the writer's reading).
/// </summary>
public sealed record SystemEntry
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>One to three characters the map prints on the system's regions, such as "UI"; derived from the name when absent.</summary>
    public string? Code { get; init; }
    public string? Role { get; init; }
    public string? Status { get; init; }
    public string? Basis { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = [];
    public IReadOnlyList<string> Docs { get; init; } = [];
}

/// <summary>Where the project is heading, naming the systems it concerns.</summary>
public sealed record DirectionEntry
{
    public string Text { get; init; } = "";
    public IReadOnlyList<string> Systems { get; init; } = [];
    public string? Basis { get; init; }
    public IReadOnlyList<string> Docs { get; init; } = [];
}

/// <summary>Something the writer found, with the paths that show it.</summary>
public sealed record FindingEntry
{
    public string Title { get; init; } = "";
    public string? Text { get; init; }
    public string? Basis { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = [];
}

/// <summary>A file's system, by its description's names.</summary>
public sealed record SystemRef(string Id, string Name, string? Code);

/// <summary>A system in brief, with what the scan finds in it.</summary>
public sealed record SystemDigest(string Id, string Name, string? Code, string? Role, string? Status, int Files, long Lines);

/// <summary>A description in brief, for a project's summary: enough to brief an agent on where things are.</summary>
public sealed record DescriptionDigest
{
    public string? Summary { get; init; }
    public IReadOnlyList<SystemDigest> Systems { get; init; } = [];
    public IReadOnlyList<string>? Direction { get; init; }
    /// <summary>The findings' titles; get_description gives them in full.</summary>
    public IReadOnlyList<string>? Findings { get; init; }
    /// <summary>Anchors that name nothing in this scan.</summary>
    public int? Stale { get; init; }
    public IReadOnlyList<UnnamedCode>? Unnamed { get; init; }
}

/// <summary>A folder or file of code and tests that no system names, with its size.</summary>
public sealed record UnnamedCode(string Path, int Files, long Lines);

/// <summary>An anchor that names nothing in the current scan.</summary>
/// <param name="Where">The system's id, "direction" or "finding", with the field: "world paths".</param>
public sealed record StaleAnchor(string Where, string Anchor, string Reason);

/// <summary>A description checked against a scan: what it says, what it no longer matches, and each file's system.</summary>
public sealed record DescriptionReport
{
    public required string File { get; init; }
    public ProjectDescription? Description { get; init; }
    /// <summary>Why it could not be read, or what is wrong with it besides stale anchors.</summary>
    public IReadOnlyList<string>? Problems { get; init; }
    public IReadOnlyList<StaleAnchor>? Stale { get; init; }
    /// <summary>Files in each system, in the description's order.</summary>
    public IReadOnlyList<int>? SystemFiles { get; init; }
    /// <summary>For each file id, the index of its system, -1 for none. Where anchors overlap, the most specific wins.</summary>
    public int[]? SystemOf { get; init; }
    /// <summary>
    /// Code and tests that no system names, largest first: the biggest folders whose code is all
    /// unnamed, and unnamed files in folders that are otherwise named. Where to look next.
    /// </summary>
    public IReadOnlyList<UnnamedCode>? Unnamed { get; init; }
}

public static partial class Descriptions
{
    public const int Version = 1;
    /// <summary>Descriptions are small; anything larger is not one.</summary>
    const long MaxBytes = 2 * 1024 * 1024;

    /// <summary>Reads a description file and checks it against the scan. Never throws: problems are reported.</summary>
    public static DescriptionReport Load(string file, ProjectIndex index)
    {
        ProjectDescription? description;
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists) return new DescriptionReport { File = file, Problems = ["The description file does not exist"] };
            if (info.Length > MaxBytes) return new DescriptionReport { File = file, Problems = ["The description file is too large to be one"] };
            description = JsonSerializer.Deserialize<ProjectDescription>(File.ReadAllText(file), ScanJson.Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new DescriptionReport { File = file, Problems = [$"Unreadable: {e.Message}"] };
        }
        return description is null ? new DescriptionReport { File = file, Problems = ["The description is empty"] } : Check(file, description, index);
    }

    /// <summary>Checks a description against the scan: its anchors, and which system each file belongs to.</summary>
    public static DescriptionReport Check(string file, ProjectDescription description, ProjectIndex index)
    {
        var problems = new List<string>();
        if (description.Floorplan != Version) problems.Add($"Format version {description.Floorplan}; this FloorPlan reads version {Version}");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var system in description.Systems)
        {
            if (system.Id.Length == 0 || system.Name.Length == 0) problems.Add("A system without an id or a name");
            else if (!ids.Add(system.Id)) problems.Add($"Two systems with the id {system.Id}");
            if (system.Code is not { } code) continue;
            if (code.Trim().Length is 0 or > 3) problems.Add($"The code of {system.Id} should have one to three characters: \"{code}\"");
            else if (!codes.Add(code.Trim())) problems.Add($"Two systems with the code {code.Trim()}");
        }
        foreach (var direction in description.Direction)
            foreach (var id in direction.Systems.Where(id => !ids.Contains(id)))
                problems.Add($"Direction names an unknown system: {id}");

        var stale = new List<StaleAnchor>();
        var files = ProjectIndex.FilesUnder(index.Tree).ToList();
        var systemOf = new int[files.Count == 0 ? 0 : files.Max(f => f.Id!.Value) + 1];
        Array.Fill(systemOf, -1);
        var specificity = new int[systemOf.Length];
        var counts = new int[description.Systems.Count];

        for (int s = 0; s < description.Systems.Count; s++)
        {
            var system = description.Systems[s];
            foreach (var text in system.Paths)
            {
                var anchor = Anchor.Parse(text);
                int matched = 0;
                foreach (var f in files)
                {
                    int score = anchor.Score(f.Path);
                    if (score < 0) continue;
                    matched++;
                    int id = f.Id!.Value;
                    // The most specific anchor wins; between equals, the system listed first.
                    if (systemOf[id] < 0 || score > specificity[id]) (systemOf[id], specificity[id]) = (s, score);
                }
                if (matched == 0) stale.Add(new StaleAnchor($"{system.Id} paths", text, "matches no file"));
            }
            foreach (var doc in system.Docs) CheckReference($"{system.Id} docs", doc, index, stale);
        }
        foreach (int s in systemOf) if (s >= 0) counts[s]++;
        foreach (var direction in description.Direction)
            foreach (var doc in direction.Docs) CheckReference("direction docs", doc, index, stale);
        foreach (var finding in description.Findings)
            foreach (var path in finding.Paths) CheckReference($"finding \"{finding.Title}\"", path, index, stale);

        return new DescriptionReport
        {
            File = file,
            Description = description,
            Problems = problems.Count > 0 ? problems : null,
            Stale = stale.Count > 0 ? stale : null,
            SystemFiles = counts,
            SystemOf = description.Systems.Count > 0 ? systemOf : null,
            Unnamed = description.Systems.Count > 0 ? UnnamedCodeIn(index, systemOf) is { Count: > 0 } unnamed ? unnamed : null : null,
        };
    }

    /// <summary>Each system with the files and lines (code, tests and documentation) the scan finds in it.</summary>
    public static IReadOnlyList<SystemDigest> Systems(DescriptionReport report, ProjectIndex index)
    {
        var systems = report.Description?.Systems ?? [];
        var files = new int[systems.Count];
        var lines = new long[systems.Count];
        if (report.SystemOf is { } of)
            foreach (var f in ProjectIndex.FilesUnder(index.Tree))
                if (of[f.Id!.Value] is int s and >= 0 && s < systems.Count)
                {
                    files[s]++;
                    if (f.Kind is { } kind && FileClassifier.IsLinesArea(kind)) lines[s] += f.Lines ?? 0;
                }
        return systems.Select((s, i) => new SystemDigest(s.Id, s.Name, s.Code, s.Role, s.Status, files[i], lines[i])).ToList();
    }

    /// <summary>The description in brief, or null when the project has none.</summary>
    public static DescriptionDigest? Digest(DescriptionReport? report, ProjectIndex index) => report?.Description is not { } d ? null : new DescriptionDigest
    {
        Summary = d.Summary,
        Systems = Systems(report, index),
        Direction = d.Direction.Count > 0 ? d.Direction.Select(x => x.Text).ToList() : null,
        Findings = d.Findings.Count > 0 ? d.Findings.Select(f => f.Title).ToList() : null,
        Stale = report.Stale?.Count,
        Unnamed = report.Unnamed?.Take(5).ToList(),
    };

    /// <summary>The system a file belongs to, or null.</summary>
    public static SystemRef? SystemOf(DescriptionReport? report, TreeNode file) =>
        report is { Description: { } d, SystemOf: { } of } && file.Id is int id && id < of.Length && of[id] is int s and >= 0
            ? new SystemRef(d.Systems[s].Id, d.Systems[s].Name, d.Systems[s].Code)
            : null;

    /// <summary>Unnamed code this large in one place makes a description out of date.</summary>
    public const int UnnamedNoteLines = 500;

    /// <summary>
    /// For a project's summary, in a line: whether its description needs writing or refreshing,
    /// and how. Null when it is current.
    /// </summary>
    public static string? Note(DescriptionReport? report)
    {
        const string how = "with describe, following the describe-project prompt";
        if (report?.Description is null)
            return report?.Problems is [var problem, ..]
                ? $"The description cannot be read ({problem}): write it again {how}."
                : $"No description yet: write one {how}, so the human sees the project's parts on the map.";
        var parts = new List<string>();
        if (report.Problems is [var first, ..]) parts.Add(first);
        if (report.Stale is { Count: > 0 } stale) parts.Add(stale.Count == 1 ? "1 anchor is stale" : $"{stale.Count} anchors are stale");
        var large = (report.Unnamed ?? []).Where(u => u.Lines >= UnnamedNoteLines).ToList();
        if (large.Count > 0)
            parts.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{large.Sum(u => u.Lines):N0} lines of code are in no system (largest: {large[0].Path})"));
        return parts.Count == 0 ? null : $"The description is out of date: {string.Join("; ", parts)}. Refresh it: get_description, then describe.";
    }

    const int UnnamedListed = 12;

    /// <summary>The largest folders whose code is all unnamed, and unnamed code files among named ones.</summary>
    static List<UnnamedCode> UnnamedCodeIn(ProjectIndex index, int[] systemOf)
    {
        var found = new List<UnnamedCode>();
        bool IsCode(TreeNode f) => f.Kind is FileKind.Code or FileKind.Test;
        // Named and unnamed code beneath a folder: (named lines, unnamed files, unnamed lines).
        (long Named, int Files, long Lines) Measure(TreeNode node)
        {
            if (node.IsFile)
            {
                if (!IsCode(node)) return (0, 0, 0);
                long lines = node.Lines ?? 0;
                return systemOf[node.Id!.Value] >= 0 ? (lines + 1, 0, 0) : (0, 1, lines);
            }
            long named = 0, unnamedLines = 0;
            int unnamedFiles = 0;
            var parts = new List<(TreeNode Node, (long Named, int Files, long Lines) M)>();
            foreach (var child in node.Children ?? [])
            {
                var m = Measure(child);
                parts.Add((child, m));
                named += m.Named;
                unnamedFiles += m.Files;
                unnamedLines += m.Lines;
            }
            // A named folder reports its unnamed parts: wholly unnamed subfolders and unnamed files.
            if (named > 0)
                foreach (var (child, m) in parts)
                    if (m.Named == 0 && m.Files > 0) found.Add(new UnnamedCode(child.Path, m.Files, m.Lines));
            return (named, unnamedFiles, unnamedLines);
        }
        var root = Measure(index.Tree);
        if (root.Named == 0 && root.Files > 0) found.Add(new UnnamedCode("", root.Files, root.Lines));
        return found.OrderByDescending(u => u.Lines).ThenBy(u => u.Path, StringComparer.Ordinal).Take(UnnamedListed).ToList();
    }

    /// <summary>A reference to a file or folder, optionally to lines of a file: it must exist, and the lines must too.</summary>
    static void CheckReference(string where, string text, ProjectIndex index, List<StaleAnchor> stale)
    {
        var anchor = Anchor.Parse(text);
        if (anchor.Pattern is not null)
        {
            if (!index.Paths.Any(p => anchor.Score(p) >= 0)) stale.Add(new StaleAnchor(where, text, "matches nothing"));
            return;
        }
        if (index.Get(anchor.Path) is not { } node) stale.Add(new StaleAnchor(where, text, "not in the project"));
        else if (anchor.To is int to && node.IsFile && node.Lines is int lines && to > lines)
            stale.Add(new StaleAnchor(where, text, $"past the end of the file, which has {lines} lines"));
    }

    [GeneratedRegex(@"^(?<path>.+?):(?<from>\d+)(?:-(?<to>\d+))?$")]
    private static partial Regex WithLines();

    /// <summary>
    /// A path anchor: an exact file or folder (a folder covers everything beneath it), or a glob
    /// where * stays within a folder name and ** crosses folders. Lines may follow a file: "a.md:18-19".
    /// </summary>
    sealed class Anchor
    {
        public string Path = "";
        public Regex? Pattern;
        public int? From, To;
        /// <summary>Characters before the first wildcard: longer is more specific.</summary>
        int literal;

        public static Anchor Parse(string text)
        {
            var anchor = new Anchor();
            var path = text.Trim().Replace('\\', '/');
            if (WithLines().Match(path) is { Success: true } m)
            {
                path = m.Groups["path"].Value;
                anchor.From = int.Parse(m.Groups["from"].Value);
                anchor.To = m.Groups["to"].Success ? int.Parse(m.Groups["to"].Value) : anchor.From;
            }
            path = path.Trim('/');
            if (path.EndsWith("/**", StringComparison.Ordinal) && path.IndexOfAny(['*', '?']) == path.Length - 2) path = path[..^3];
            anchor.Path = path;
            int wild = path.IndexOfAny(['*', '?']);
            anchor.literal = wild < 0 ? path.Length : wild;
            if (wild >= 0) anchor.Pattern = new Regex("^" + Glob(path) + "$", RegexOptions.CultureInvariant);
            return anchor;
        }

        static string Glob(string path)
        {
            var pattern = new System.Text.StringBuilder();
            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];
                if (c == '*' && i + 1 < path.Length && path[i + 1] == '*')
                {
                    // "**/" also matches no folder at all.
                    bool slash = i + 2 < path.Length && path[i + 2] == '/';
                    pattern.Append(slash ? "(?:.*/)?" : ".*");
                    i += slash ? 2 : 1;
                }
                else if (c == '*') pattern.Append("[^/]*");
                else if (c == '?') pattern.Append("[^/]");
                else pattern.Append(Regex.Escape(c.ToString()));
            }
            return pattern.ToString();
        }

        /// <summary>How specifically this anchor names the path; -1 when it does not. An exact file wins over any folder or glob.</summary>
        public int Score(string path)
        {
            if (Pattern is not null) return Pattern.IsMatch(path) ? literal : -1;
            if (path == Path) return int.MaxValue;
            return Path.Length == 0 || path.StartsWith(Path + "/", StringComparison.Ordinal) ? literal : -1;
        }
    }
}

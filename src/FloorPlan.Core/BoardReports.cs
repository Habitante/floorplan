using System.Globalization;
using System.Text.Json;

namespace FloorPlan.Core;

// What agents receive from the board tools, serialised with ScanJson.Options. Measurements keep
// the map's meaning: "lines" is physical lines in code and documentation files, the Lines area.

public sealed record RangeReport
{
    public int Commits { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    /// <summary>The range ends at the newest commit.</summary>
    public bool Latest { get; init; }
}

public sealed record NodeReport
{
    public required string Path { get; init; }
    /// <summary>"file", "folder", or "gone" when it is no longer in the scan.</summary>
    public required string Type { get; init; }
    public string? Kind { get; init; }
    public int? Files { get; init; }
    public long? Lines { get; init; }
    public long? Bytes { get; init; }
    public int? CommitsInRange { get; init; }
    /// <summary>A file's uncommitted change.</summary>
    public string? Uncommitted { get; init; }
    /// <summary>Files with uncommitted changes inside a folder.</summary>
    public int? UncommittedFiles { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset? SelectedAt { get; init; }
    public long? SecondsAgo { get; init; }
    /// <summary>HEAD when it was selected, when HEAD has moved since.</summary>
    public string? SelectedAtHead { get; init; }
}

public sealed record ReaderReport
{
    public required string Path { get; init; }
    /// <summary>"file", "uncommitted diff" or "diff of commit &lt;sha&gt;"; line numbers refer to that version.</summary>
    public required string Showing { get; init; }
    public int[]? VisibleLines { get; init; }
    public int[]? SelectedLines { get; init; }
    public DateTimeOffset? OpenedAt { get; init; }
    public long? SecondsAgo { get; init; }
}

public sealed record CommitReport
{
    public required string Sha { get; init; }
    public string? Date { get; init; }
    public string? Author { get; init; }
    public required string Subject { get; init; }
    /// <summary>Files it touched (inside the summarised path, for a summary).</summary>
    public int? Files { get; init; }
}

public sealed record KindReport(int Files, long? Lines, long Bytes);

public sealed record SelectionReport
{
    public bool Window { get; init; } = true;
    public required string Project { get; init; }
    public string? Head { get; init; }
    public NodeReport? Selected { get; init; }
    public ReaderReport? Reader { get; init; }
    /// <summary>The folder the map is zoomed into; "" for the whole project.</summary>
    public required string Zoom { get; init; }
    /// <summary>The commit selected on the history strip.</summary>
    public CommitReport? Commit { get; init; }
    public RangeReport? Range { get; init; }
    public required string Area { get; init; }
    public required string Fill { get; init; }
    public IReadOnlyList<NodeReport>? Recent { get; init; }
}

public sealed record SummaryReport
{
    /// <summary>True when measured from the open window's model and history range.</summary>
    public bool Window { get; init; }
    public required string Project { get; init; }
    public required string Path { get; init; }
    public required string Type { get; init; }
    public string? Kind { get; init; }
    public int Files { get; init; }
    public long? Lines { get; init; }
    public long? Bytes { get; init; }
    public int? Unmeasured { get; init; }
    public string? Note { get; init; }
    public IReadOnlyDictionary<string, KindReport>? Composition { get; init; }
    public RangeReport? Range { get; init; }
    public int? CommitsInRange { get; init; }
    public IReadOnlyList<string>? Uncommitted { get; init; }
    public int? UncommittedNotListed { get; init; }
    public IReadOnlyList<NodeReport>? Largest { get; init; }
    public IReadOnlyList<CommitReport>? RecentCommits { get; init; }
}

public sealed record ShowReport
{
    /// <summary>True when a window showed the pointer.</summary>
    public bool Window { get; init; }
    public IReadOnlyList<string> Shown { get; init; } = [];
    public IReadOnlyList<string>? Unknown { get; init; }
    public string? Note { get; init; }
}

public static class BoardReports
{
    public const int LargestLimit = 12;
    public const int CommitLimit = 10;
    public const int UncommittedLimit = 25;

    public static string Date(long unixSeconds) =>
        DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Short(string sha) => sha.Length > 10 ? sha[..10] : sha;

    static string Name<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    /// <summary>A file's own measurements, or a folder's totals.</summary>
    public static NodeReport Node(TreeNode node, CommitWindow? window)
    {
        int changed = node.IsFile ? 0 : ProjectIndex.FilesUnder(node).Count(f => f.Change is not null);
        return new NodeReport
        {
            Path = node.Path,
            Type = node.IsFile ? "file" : "folder",
            Kind = node.Kind is { } kind ? Name(kind) : null,
            Files = node.IsFile ? null : node.Files,
            Lines = node.IsFile ? node.Lines : (node.Code ?? 0) + (node.Docs ?? 0),
            Bytes = node.Bytes,
            CommitsInRange = window?.Count(node),
            Uncommitted = node.Change is { } change ? Name(change) : null,
            UncommittedFiles = changed > 0 ? changed : null,
            Note = node.Note,
        };
    }

    public static SelectionReport Selection(ProjectIndex index, BoardState state, DateTimeOffset now)
    {
        var project = index.Scan.Project;
        var view = state.View;
        var window = project.Git ? CommitWindow.For(index.Scan, view.Range) : null;

        NodeReport Stamped(Choice choice, bool measure)
        {
            var node = index.Get(choice.Path);
            var report = node is null
                ? new NodeReport { Path = choice.Path, Type = "gone", Note = "No longer in the scan" }
                : measure ? Node(node, window) : new NodeReport { Path = node.Path, Type = node.IsFile ? "file" : "folder" };
            return report with
            {
                SelectedAt = choice.At,
                SecondsAgo = Seconds(now - choice.At),
                SelectedAtHead = choice.Head is not null && choice.Head != project.Head ? Short(choice.Head) : null,
            };
        }

        ReaderReport? reader = null;
        if (view.Reader is { } open)
        {
            reader = new ReaderReport
            {
                Path = open.Path,
                Showing = open.Commit switch
                {
                    null => "file",
                    "working" => "uncommitted diff",
                    var sha => $"diff of commit {Short(sha)}",
                },
                VisibleLines = Lines(open.Visible),
                SelectedLines = Lines(open.Selection),
                OpenedAt = state.Reading?.At,
                SecondsAgo = state.Reading is { } r ? Seconds(now - r.At) : null,
            };
        }

        CommitReport? commit = view.Commit switch
        {
            null => null,
            "working" => new CommitReport
            {
                Sha = "working",
                Subject = "Uncommitted changes",
                Files = ProjectIndex.FilesUnder(index.Tree).Count(f => f.Change is not null),
            },
            var sha => index.Scan.Commits.FirstOrDefault(c => c.Sha.StartsWith(sha, StringComparison.OrdinalIgnoreCase)) is { } c
                ? new CommitReport { Sha = Short(c.Sha), Date = Date(c.Time), Author = c.Author, Subject = c.Subject, Files = c.Files.Length + c.Other }
                : null,
        };

        return new SelectionReport
        {
            Project = index.Root,
            Head = project.Head is { } head ? Short(head) : null,
            Selected = state.Selected is { } selected ? Stamped(selected, measure: true) : null,
            Reader = reader,
            Zoom = view.Focus,
            Commit = commit,
            Range = window?.Report(),
            Area = view.Area,
            Fill = view.Fill,
            Recent = state.Recent.Count > 0 ? state.Recent.Select(r => Stamped(r, measure: false)).ToList() : null,
        };
    }

    /// <summary>Footprint, composition and activity of a file or folder.</summary>
    public static SummaryReport Summary(ProjectIndex index, TreeNode node, CommitWindow? window, string area, bool fromWindow)
    {
        var files = ProjectIndex.FilesUnder(node).ToList();
        var composition = files
            .GroupBy(f => f.Kind ?? FileKind.Other)
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => Name(g.Key),
                g => new KindReport(g.Count(), FileClassifier.CountsLines(g.Key) ? g.Sum(f => (long)(f.Lines ?? 0)) : null, g.Sum(f => f.Bytes ?? 0)));
        var changed = files.Where(f => f.Change is not null).Select(f => f.Path).Order(StringComparer.Ordinal).ToList();

        List<NodeReport>? largest = null;
        if (!node.IsFile)
        {
            Func<TreeNode, long> weight = area == "bytes"
                ? n => n.Bytes ?? 0
                : n => n.IsFile ? (n.Kind is FileKind.Code or FileKind.Docs ? n.Lines ?? 0 : 0) : (n.Code ?? 0) + (n.Docs ?? 0);
            largest = (node.Children ?? [])
                .Where(c => weight(c) > 0)
                .OrderByDescending(weight)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .Take(LargestLimit)
                .Select(c => Node(c, window))
                .ToList();
        }

        int? inRange = null;
        List<CommitReport>? recent = null;
        if (window is not null)
        {
            var ids = files.Select(f => f.Id!.Value).ToHashSet();
            var touching = window.Commits.Where(c => c.Files.Any(ids.Contains)).ToList();
            inRange = touching.Count;
            recent = touching.Take(CommitLimit).Select(c => new CommitReport
            {
                Sha = Short(c.Sha),
                Date = Date(c.Time),
                Author = c.Author,
                Subject = c.Subject,
                Files = c.Files.Count(ids.Contains),
            }).ToList();
        }

        var measures = Node(node, null);
        return new SummaryReport
        {
            Window = fromWindow,
            Project = index.Root,
            Path = node.Path,
            Type = measures.Type,
            Kind = measures.Kind,
            Files = files.Count,
            Lines = measures.Lines,
            Bytes = measures.Bytes,
            Unmeasured = node.Unmeasured is > 0 ? node.Unmeasured : null,
            Note = node.Note,
            Composition = node.IsFile ? null : composition,
            Range = window?.Report(),
            CommitsInRange = inRange,
            Uncommitted = changed.Count > 0 ? changed.Take(UncommittedLimit).ToList() : null,
            UncommittedNotListed = changed.Count > UncommittedLimit ? changed.Count - UncommittedLimit : null,
            Largest = largest is { Count: > 0 } ? largest : null,
            RecentCommits = recent is { Count: > 0 } ? recent : null,
        };
    }

    static long Seconds(TimeSpan span) => Math.Max(0, (long)span.TotalSeconds);

    static int[]? Lines(int[]? range) => range is [var first, var last] && first > 0 && last >= first ? [first, last] : null;
}

namespace FloorPlan.Core;

public enum FileKind { Code, Docs, Data, Asset, Other }

public enum EntryStatus
{
    Ok,
    /// <summary>Tracked by Git but absent from the working tree.</summary>
    Missing,
    Unreadable,
    /// <summary>Symbolic link or junction; not followed.</summary>
    Link,
    /// <summary>Nested repository or submodule; not traversed.</summary>
    Repository,
}

/// <summary>Uncommitted state of a file relative to HEAD, from <c>git status</c>.</summary>
public enum WorkingChange { Modified, Added, Deleted, Renamed, Untracked }

public sealed class ProjectScan
{
    public int Version { get; init; } = 1;
    public required ProjectInfo Project { get; init; }
    public required TreeNode Tree { get; init; }
    public required IReadOnlyList<CommitInfo> Commits { get; init; }
    public required IReadOnlyList<ScanIssue> Issues { get; init; }
}

public sealed class ProjectInfo
{
    public required string Root { get; init; }
    public required string Name { get; init; }
    /// <summary>"tracked" or "tracked+untracked" for Git projects, "folder" for a plain folder.</summary>
    public required string Scope { get; init; }
    public bool Git { get; init; }
    public string? Head { get; init; }
    public string? Branch { get; init; }
    /// <summary>The repository's Git directory, watched for commits and checkouts.</summary>
    public string? GitDirectory { get; init; }
    public int CommitLimit { get; init; }
    /// <summary>True when every reachable non-merge commit touching the scope was read.</summary>
    public bool HistoryComplete { get; init; }
    /// <summary>Files with uncommitted changes, including untracked files outside the scope.</summary>
    public int Changes { get; init; }
    /// <summary>Untracked, non-ignored files not shown because the scope excludes them.</summary>
    public int UntrackedHidden { get; init; }
    public DateTimeOffset ScannedAt { get; init; }
    public long DurationMs { get; init; }
}

/// <summary>
/// A folder or file. Files carry their own measurements; folders carry totals over the
/// files beneath them. Null means unavailable, never zero.
/// </summary>
public sealed class TreeNode
{
    public required string Name { get; init; }
    /// <summary>Project-relative path with forward slashes; empty for the root.</summary>
    public required string Path { get; init; }

    // Files
    public int? Id { get; set; }
    public FileKind? Kind { get; set; }
    public EntryStatus? Status { get; set; }
    public int? Lines { get; set; }
    public WorkingChange? Change { get; set; }
    public string? Note { get; set; }

    // Both: a folder's bytes are the sum of its measured files; its date is the latest beneath it.
    public long? Bytes { get; set; }
    public DateTimeOffset? Modified { get; set; }

    // Folders
    public int? Files { get; set; }
    public long? Code { get; set; }
    public long? Docs { get; set; }
    public long? Data { get; set; }
    /// <summary>Files whose size, or recognised line count, is unavailable.</summary>
    public int? Unmeasured { get; set; }
    public List<TreeNode>? Children { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFile => Id is not null;
}

public sealed class CommitInfo
{
    public required string Sha { get; init; }
    /// <summary>Committer time, Unix seconds.</summary>
    public long Time { get; init; }
    public required string Author { get; init; }
    public required string Subject { get; init; }
    /// <summary>Ids of current files this commit touched.</summary>
    public required int[] Files { get; init; }
    /// <summary>Touched paths that are not in the current inventory (deleted or renamed since).</summary>
    public int Other { get; init; }
}

public sealed record ScanIssue(string? Path, string Message);

public sealed record ScanProgress(string Phase, int Done, int Total);

public sealed record ScanOptions
{
    public int CommitLimit { get; init; } = 1000;
    public long MaxLineCountBytes { get; init; } = 8 * 1024 * 1024;
    public int Concurrency { get; init; } = Math.Clamp(Environment.ProcessorCount, 2, 16);
    /// <summary>Also show untracked files that Git does not ignore.</summary>
    public bool IncludeUntracked { get; init; }
    public IReadOnlySet<string> FolderExclusions { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", "node_modules", ".vs", "__pycache__", ".venv" };
}

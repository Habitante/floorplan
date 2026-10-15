namespace FloorPlan.Core;

/// <summary>What the human's window shows, as its front end reports it.</summary>
public sealed class BoardView
{
    /// <summary>The folder the map is zoomed into; "" for the whole project.</summary>
    public string Focus { get; init; } = "";
    /// <summary>The selected file or folder.</summary>
    public string? Selected { get; init; }
    /// <summary>The commit selected on the history strip; "working" for the uncommitted changes.</summary>
    public string? Commit { get; init; }
    /// <summary>The commits counted by the activity lens.</summary>
    public BoardRange? Range { get; init; }
    /// <summary>The file open in the reader.</summary>
    public BoardReader? Reader { get; init; }
    public string Area { get; init; } = "lines";
    public string Fill { get; init; } = "activity";
}

/// <param name="From">Oldest commit in the range.</param>
/// <param name="To">Newest commit in the range.</param>
/// <param name="Latest">The range ends at the newest commit and follows new ones.</param>
public sealed record BoardRange(string? From, string? To, int Count, bool Latest);

/// <param name="Commit">A SHA when the reader shows that commit's diff; "working" for the uncommitted diff.</param>
/// <param name="Visible">First and last line in view.</param>
/// <param name="Selection">First and last line of the selected text.</param>
public sealed record BoardReader(string Path, string? Commit, int[]? Visible, int[]? Selection);

/// <summary>A file or folder the human chose, when, and the HEAD at that moment.</summary>
public sealed record Choice(string Path, DateTimeOffset At, string? Head);

/// <summary>An agent's pointer: files and folders outlined on the map with a caption, until replaced or dismissed.</summary>
public sealed record Pointer(string Agent, string Caption, IReadOnlyList<string> Paths, DateTimeOffset At);

/// <summary>
/// The window's half of the shared board: the latest view, when its selection was made and at
/// which HEAD, and the selections before it. The human selects, then types a question; by the
/// time an agent asks, the selection may have moved, and these let "this" keep its meaning.
/// </summary>
public sealed class BoardState
{
    public const int RecentLimit = 5;
    readonly List<Choice> recent = [];

    public BoardView View { get; private set; } = new();
    public Choice? Selected { get; private set; }
    public Choice? Reading { get; private set; }
    /// <summary>Earlier selections, newest first.</summary>
    public IReadOnlyList<Choice> Recent => recent;

    public void Update(BoardView view, string? head, DateTimeOffset now)
    {
        if (view.Selected != Selected?.Path)
        {
            if (Selected is not null) Remember(Selected);
            Selected = view.Selected is null ? null : new Choice(view.Selected, now, head);
        }
        var reader = view.Reader;
        if (reader?.Path != Reading?.Path || reader?.Commit != View.Reader?.Commit)
            Reading = reader is null ? null : new Choice(reader.Path, now, head);
        View = view;
    }

    public void Reset()
    {
        View = new BoardView();
        Selected = null;
        Reading = null;
        recent.Clear();
    }

    void Remember(Choice choice)
    {
        recent.RemoveAll(r => r.Path == choice.Path);
        recent.Insert(0, choice);
        if (recent.Count > RecentLimit) recent.RemoveAt(recent.Count - 1);
    }
}

/// <summary>The commits an activity count covers: the human's history range, or the latest ones.</summary>
public sealed class CommitWindow
{
    /// <summary>Same default as the front end's history strip.</summary>
    public const int DefaultCount = 40;

    CommitWindow(IReadOnlyList<CommitInfo> commits, bool latest)
    {
        Commits = commits;
        Latest = latest;
    }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<CommitInfo> Commits { get; }
    public bool Latest { get; }

    /// <summary>The range the human chose, or the latest commits when there is none or it no longer resolves.</summary>
    public static CommitWindow For(ProjectScan scan, BoardRange? range)
    {
        var all = scan.Commits;
        if (range is { Latest: false, From: { } from, To: { } to })
        {
            int newest = IndexOf(all, to);
            int oldest = IndexOf(all, from);
            if (newest >= 0 && oldest >= newest) return new CommitWindow(all.Skip(newest).Take(oldest - newest + 1).ToList(), false);
        }
        int count = range is { Latest: true, Count: > 0 } ? range.Count : DefaultCount;
        return new CommitWindow(all.Take(count).ToList(), true);
    }

    static int IndexOf(IReadOnlyList<CommitInfo> commits, string sha)
    {
        for (int i = 0; i < commits.Count; i++)
            if (commits[i].Sha.StartsWith(sha, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>Commits that touch the node or anything beneath it, each counted once.</summary>
    public int Count(TreeNode node)
    {
        var ids = Ids(node);
        return Commits.Count(c => c.Files.Any(ids.Contains));
    }

    public static HashSet<int> Ids(TreeNode node) => ProjectIndex.FilesUnder(node).Select(f => f.Id!.Value).ToHashSet();

    public RangeReport Report() => new()
    {
        Commits = Commits.Count,
        From = Commits.Count > 0 ? BoardReports.Date(Commits[^1].Time) : null,
        To = Commits.Count > 0 ? BoardReports.Date(Commits[0].Time) : null,
        Latest = Latest,
    };
}

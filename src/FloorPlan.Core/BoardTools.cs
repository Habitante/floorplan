using System.Text;
using System.Text.Json;

namespace FloorPlan.Core;

/// <summary>A window's side of the board, as <see cref="BoardService"/> needs it.</summary>
public interface IBoardSurface
{
    /// <summary>The project as the window last scanned it; null before the first scan.</summary>
    ProjectIndex? Index { get; }
    BoardState State { get; }
    /// <summary>Shows an agent's pointer on the map, replacing its previous one; no paths clears it.</summary>
    void Point(Pointer pointer);
}

/// <summary>Answers board requests in the window, from the model and view the human sees.</summary>
public static class BoardService
{
    public const int MaxPaths = 500;
    public const int MaxCaption = 280;
    public const int MaxAgent = 60;

    public static BoardReply Handle(IBoardSurface surface, BoardRequest request, DateTimeOffset now)
    {
        if (surface.Index is not { } index) return BoardReply.Failure("The FloorPlan window has not finished scanning its project");
        switch (request.Op)
        {
            case "selection":
                return BoardReply.Success(BoardReports.Selection(index, surface.State, now));
            case "show":
                return Show(surface, index, request, now);
            case "summary":
            {
                if (index.Find(request.Path ?? "", request.Base) is not { } node)
                    return BoardReply.Failure($"Not in the scanned project: {request.Path}");
                var view = surface.State.View;
                var window = index.Scan.Project.Git ? CommitWindow.For(index.Scan, view.Range) : null;
                return BoardReply.Success(BoardReports.Summary(index, node, window, view.Area, fromWindow: true));
            }
            default:
                return BoardReply.Failure($"Unknown request: {request.Op}");
        }
    }

    static BoardReply Show(IBoardSurface surface, ProjectIndex index, BoardRequest request, DateTimeOffset now)
    {
        var requested = request.Paths ?? [];
        if (requested.Count > MaxPaths) return BoardReply.Failure($"Point at {MaxPaths} paths or fewer; their common folder says more");

        var shown = new List<string>();
        var unknown = new List<string>();
        foreach (var path in requested)
        {
            // The whole project is the map itself; there is nothing to outline.
            if (index.Find(path, request.Base) is not { Path.Length: > 0 } node) unknown.Add(path);
            else if (!shown.Contains(node.Path)) shown.Add(node.Path);
        }
        if (requested.Count > 0 && shown.Count == 0)
        {
            return BoardReply.Success(new ShowReport
            {
                Window = true,
                Unknown = unknown,
                Note = "None of these paths are in the scanned project, so the pointer did not change",
            });
        }

        var agent = Clean(request.Agent, MaxAgent);
        surface.Point(new Pointer(agent.Length > 0 ? agent : "Agent", Clean(request.Caption, MaxCaption), shown, now));
        return BoardReply.Success(new ShowReport
        {
            Window = true,
            Shown = shown,
            Unknown = unknown.Count > 0 ? unknown : null,
            Note = shown.Count == 0 ? "Pointer cleared" : null,
        });
    }

    /// <summary>One line of text without control characters, shortened to the limit.</summary>
    public static string Clean(string? text, int limit)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var builder = new StringBuilder();
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var visible = new string(word.Where(c => !char.IsControl(c)).ToArray());
            if (visible.Length == 0) continue;
            if (builder.Length > 0) builder.Append(' ');
            builder.Append(visible);
            if (builder.Length > limit) break;
        }
        return builder.Length > limit ? builder.ToString(0, limit - 1).TrimEnd() + "…" : builder.ToString();
    }
}

public sealed record BoardToolResult(string Json, bool IsError);

/// <summary>
/// What <c>floorplan mcp</c> offers agents. Each tool asks the window showing the agent's project.
/// Without a window, selection and pointing say that nobody is looking, so the agent falls back
/// to prose, and summaries come from a fresh read-only scan.
/// </summary>
public sealed class BoardTools(string folder, GitRunner git, Func<string, ScanOptions> scanOptions)
{
    public string Folder { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    public async Task<BoardToolResult> SelectionAsync(CancellationToken cancellationToken = default) =>
        await AskAsync(new BoardRequest { Op = "selection", Base = Folder }, cancellationToken) ?? Result(new
        {
            window = false,
            note = "No FloorPlan window is showing this project, so nothing is selected; ask the human what they mean.",
        });

    public async Task<BoardToolResult> ShowAsync(IReadOnlyList<string> paths, string? caption, string? agent, CancellationToken cancellationToken = default) =>
        await AskAsync(new BoardRequest { Op = "show", Paths = paths, Caption = caption, Agent = agent, Base = Folder }, cancellationToken) ?? Result(new ShowReport
        {
            Window = false,
            Note = "No FloorPlan window is showing this project, so nobody saw the pointer; name the files in text instead.",
        });

    public async Task<BoardToolResult> SummaryAsync(string? path, CancellationToken cancellationToken = default)
    {
        if (await AskAsync(new BoardRequest { Op = "summary", Path = path, Base = Folder }, cancellationToken) is { } asked) return asked;
        try
        {
            var root = await ProjectRootAsync(cancellationToken);
            var scan = await new ProjectScanner(git).ScanAsync(root, scanOptions(root), null, cancellationToken);
            var index = new ProjectIndex(scan);
            if (index.Find(path ?? "", Folder) is not { } node) return Error($"Not in the scanned project: {path}");
            var window = scan.Project.Git ? CommitWindow.For(scan, null) : null;
            return Result(BoardReports.Summary(index, node, window, "lines", fromWindow: false));
        }
        catch (Exception e) when (e is DirectoryNotFoundException or GitException or IOException or UnauthorizedAccessException)
        {
            return Error(e.Message);
        }
    }

    /// <summary>The window's answer; null when no window is showing the project.</summary>
    async Task<BoardToolResult?> AskAsync(BoardRequest request, CancellationToken cancellationToken)
    {
        BoardReply? reply;
        try
        {
            reply = await BoardChannel.RequestAsync(Folder, request, cancellationToken);
        }
        catch (Exception e) when ((e is IOException or JsonException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return Error($"The FloorPlan window did not answer: {e.Message}");
        }
        if (reply is null) return null;
        return reply.Ok && reply.Result is { } result ? new BoardToolResult(result.GetRawText(), false) : Error(reply.Error ?? "The FloorPlan window refused the request");
    }

    /// <summary>The repository's top folder, or the folder itself outside a repository.</summary>
    async Task<string> ProjectRootAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await git.RunAsync(Folder, ["rev-parse", "--show-toplevel"], cancellationToken);
            if (result.ExitCode == 0 && result.Text.Length > 0) return Path.GetFullPath(result.Text);
        }
        catch (GitUnavailableException)
        {
        }
        return Folder;
    }

    static BoardToolResult Result(object value) => new(JsonSerializer.Serialize(value, ScanJson.Options), false);

    static BoardToolResult Error(string message) => new(JsonSerializer.Serialize(new { error = message }, ScanJson.Options), true);
}

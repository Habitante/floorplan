using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FloorPlan.Core;

/// <summary>A window's side of the board, as <see cref="BoardService"/> needs it.</summary>
public interface IBoardSurface
{
    /// <summary>The project as the window last scanned it; null before the first scan.</summary>
    ProjectIndex? Index { get; }
    BoardState State { get; }
    /// <summary>True while the window has keyboard focus.</summary>
    bool Focused { get; }
    /// <summary>The project's description as the window last checked it against its scan; null when it has none.</summary>
    DescriptionReport? Description { get; }
    /// <summary>Shows an agent's pointer, replacing its previous one; no items clears it. Reveal also brings it into view.</summary>
    void Point(Pointer pointer, bool reveal);
    /// <summary>Brings the window above other windows without taking keyboard focus.</summary>
    void Raise();
}

/// <summary>Answers board requests in the window, from the model and view the human sees.</summary>
public static partial class BoardService
{
    public const int MaxPaths = 500;
    public const int MaxCaption = 280;
    public const int MaxAgent = 60;

    public static BoardReply Handle(IBoardSurface surface, BoardRequest request, DateTimeOffset now)
    {
        switch (request.Op)
        {
            case "hello":
                return BoardReply.Success(new HelloReport(
                    surface.Index?.Root, surface.Index?.Scan.Project.Name, Seconds(now - surface.State.LastActive), surface.Focused));
            case "raise":
                surface.Raise();
                return BoardReply.Success(new { raised = true });
        }

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
                    return BoardReply.Failure($"Not in the scanned project {index.Root}: {request.Path}");
                var view = surface.State.View;
                var window = index.Scan.Project.Git ? CommitWindow.For(index.Scan, view.Range) : null;
                var summary = BoardReports.Summary(index, node, window, view.Area, fromWindow: true, surface.Description);
                if (node.Path.Length == 0) summary = summary with { SinceLastSeen = BoardReports.Shown(surface.State.SinceLastSeen, now) };
                return BoardReply.Success(summary);
            }
            default:
                return BoardReply.Failure($"Unknown request: {request.Op}");
        }
    }

    static BoardReply Show(IBoardSurface surface, ProjectIndex index, BoardRequest request, DateTimeOffset now)
    {
        var requested = request.Paths ?? [];
        if (requested.Count > MaxPaths) return BoardReply.Failure($"Point at {MaxPaths} paths or fewer; their common folder says more");

        var items = new List<PointerItem>();
        var offMap = new List<OffMap>();
        var unknown = new List<string>();
        foreach (var written in requested)
        {
            var (path, from, to) = SplitLines(written);
            // The whole project is the map itself; there is nothing to outline.
            if (index.Find(path, request.Base) is { Path.Length: > 0 } node)
            {
                var item = node.IsFile && from is not null ? new PointerItem(node.Path, from, to) : new PointerItem(node.Path);
                if (!items.Contains(item)) items.Add(item);
            }
            else if (index.FindOnDisk(path, request.Base) is { } relative)
            {
                // Ignored or untracked: outline the nearest folder on the map instead.
                var nearest = index.Nearest(relative);
                offMap.Add(new OffMap(relative, nearest.Path.Length > 0 ? nearest.Path : null));
                if (nearest.Path.Length > 0) items.Add(new PointerItem(nearest.Path, Note: relative));
            }
            else
            {
                unknown.Add(written);
            }
        }

        if (requested.Count > 0 && items.Count == 0)
        {
            return BoardReply.Success(new ShowReport
            {
                Window = true,
                Project = index.Root,
                NotOnMap = offMap.Count > 0 ? offMap : null,
                Unknown = unknown.Count > 0 ? unknown : null,
                Note = "Nothing of this is on the map, so the pointer did not change",
            });
        }

        var agent = Clean(request.Agent, MaxAgent);
        bool reveal = request.Reveal && items.Count > 0;
        surface.Point(new Pointer(agent.Length > 0 ? agent : "Agent", Clean(request.Caption, MaxCaption), items, now), reveal);
        if (reveal) surface.Raise();
        return BoardReply.Success(new ShowReport
        {
            Window = true,
            Project = index.Root,
            Shown = items.Where(i => i.Note is null).Select(Describe).ToList(),
            NotOnMap = offMap.Count > 0 ? offMap : null,
            Unknown = unknown.Count > 0 ? unknown : null,
            Note = items.Count == 0 ? "Pointer cleared" : null,
        });
    }

    static string Describe(PointerItem item) =>
        item.From is not { } from ? item.Path : item.To is { } to && to != from ? $"{item.Path}:{from}-{to}" : $"{item.Path}:{from}";

    [GeneratedRegex(@"^(?<path>.+?)(?:#L(?<from>\d{1,7})(?:-L?(?<to>\d{1,7}))?|:(?<from>\d{1,7})(?:-(?<to>\d{1,7}))?)$")]
    private static partial Regex LineRange();

    /// <summary>
    /// Splits a line range off a path: "scenes/world.gd:480-492", ":480" or "#L480-L492". A drive
    /// letter's colon is not a line number.
    /// </summary>
    public static (string Path, int? From, int? To) SplitLines(string written)
    {
        var text = written.Trim();
        var match = LineRange().Match(text);
        if (!match.Success) return (text, null, null);
        var path = match.Groups["path"].Value;
        int from = int.Parse(match.Groups["from"].Value);
        int to = match.Groups["to"].Success ? int.Parse(match.Groups["to"].Value) : from;
        if ((path.Length == 1 && char.IsAsciiLetter(path[0])) || from < 1 || to < from) return (text, null, null);
        return (path, from, to);
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

    static long Seconds(TimeSpan span) => Math.Max(0, (long)span.TotalSeconds);
}

public sealed record BoardToolResult(string Json, bool IsError);

/// <summary>A project's description and its check, as get_description and describe report it.</summary>
public sealed record DescriptionReply
{
    public required string Project { get; init; }
    /// <summary>Where the description is kept: FloorPlan's own data, outside the project.</summary>
    public required string File { get; init; }
    /// <summary>For describe: whether it was saved.</summary>
    public bool? Saved { get; init; }
    public ProjectDescription? Description { get; init; }
    public IReadOnlyList<string>? Problems { get; init; }
    public IReadOnlyList<StaleAnchor>? Stale { get; init; }
    /// <summary>What each system holds in this scan.</summary>
    public IReadOnlyList<SystemDigest>? Systems { get; init; }
    /// <summary>Code and tests that no system names yet, largest first.</summary>
    public IReadOnlyList<UnnamedCode>? Unnamed { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// What <c>floorplan mcp</c> offers agents. One server serves every project: each call finds the
/// open windows and asks the one for the named project, else the one an absolute path points
/// into, else the one the human used last. Without a window, selection and pointing say that
/// nobody is looking, summaries come from a fresh read-only scan, and <c>open</c> starts one.
/// </summary>
/// <param name="folder">The agent's working folder: the base of relative paths and the default project.</param>
/// <param name="startWindow">Starts a window on a folder; false when it could not.</param>
/// <param name="seen">The HEAD the human last saw on a project root, for summaries without a window.</param>
/// <param name="descriptionPath">Where a project root's description is kept, outside the project; null keeps none.</param>
public sealed class BoardTools(
    string folder, GitRunner git, Func<string, ScanOptions> scanOptions, Func<string, bool>? startWindow = null, Func<string, SeenMark?>? seen = null,
    Func<string, string>? descriptionPath = null)
{
    static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(20);

    public string Folder { get; } = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

    public async Task<BoardToolResult> SelectionAsync(string? project = null, CancellationToken cancellationToken = default)
    {
        var (open, targets) = await TargetsAsync(project, null, cancellationToken);
        if (targets.Count == 0) return NoWindow(project, open, "so nothing is selected; ask the human what they mean.");

        var (result, error) = await AskAsync(targets[0], new BoardRequest { Op = "selection", Base = Folder }, cancellationToken);
        if (result is null) return Error(error!);
        var others = open.Where(w => w.Project is not null && w != targets[0]).Select(w => new WindowReport(w.Project!, w.IdleSeconds, w.Focused)).ToList();
        if (others.Count > 0) result["otherWindows"] = JsonSerializer.SerializeToNode(others, ScanJson.Options);
        return new BoardToolResult(result.ToJsonString(ScanJson.Options), false);
    }

    public async Task<BoardToolResult> ShowAsync(
        IReadOnlyList<string> paths, string? caption, string? agent, string? project = null, bool reveal = false, CancellationToken cancellationToken = default)
    {
        var (open, targets) = await TargetsAsync(project, paths, cancellationToken);
        if (targets.Count == 0)
            return NoWindow(project, open, "so nobody saw the pointer; name the files in text, or call open if the human wants to see them.");

        // Every window on the project draws the pointer; the one used last also reveals it.
        JsonObject? report = null;
        string? error = null;
        int drawn = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            var request = new BoardRequest { Op = "show", Paths = paths, Caption = caption, Agent = agent, Base = Folder, Reveal = reveal && i == 0 };
            var (result, problem) = await AskAsync(targets[i], request, cancellationToken);
            if (result is null)
            {
                error ??= problem;
                continue;
            }
            report ??= result;
            drawn++;
        }
        if (report is null) return Error(error!);
        report["windows"] = drawn;
        await ExplainOffMapAsync(report, cancellationToken);
        return new BoardToolResult(report.ToJsonString(ScanJson.Options), false);
    }

    public async Task<BoardToolResult> SummaryAsync(string? path, string? project = null, CancellationToken cancellationToken = default)
    {
        var (_, targets) = await TargetsAsync(project, path is null ? null : [path], cancellationToken);
        if (targets.Count > 0)
        {
            var (result, error) = await AskAsync(targets[0], new BoardRequest { Op = "summary", Path = path, Base = Folder }, cancellationToken);
            return result is null ? Error(error!) : new BoardToolResult(result.ToJsonString(ScanJson.Options), false);
        }
        if (ProjectFolder(project) is not { } folder) return Error($"No open window or folder for the project \"{project}\"");
        try
        {
            var root = await ProjectRootAsync(folder, cancellationToken);
            var scan = await new ProjectScanner(git).ScanAsync(root, scanOptions(root), null, cancellationToken);
            var index = new ProjectIndex(scan);
            if (index.Find(path ?? "", Folder) is not { } node) return Error($"Not in the scanned project {root}: {path}");
            var window = scan.Project.Git ? CommitWindow.For(scan, null) : null;
            var description = descriptionPath?.Invoke(root) is { } file && File.Exists(file) ? Descriptions.Load(file, index) : null;
            var report = BoardReports.Summary(index, node, window, "lines", fromWindow: false, description);
            // What landed since the human last used a window on the project.
            if (node.Path.Length == 0 && seen?.Invoke(root) is { } mark)
                report = report with { SinceLastSeen = await Since.ComputeAsync(git, index, mark, cancellationToken) };
            return Result(report);
        }
        catch (Exception e) when (e is DirectoryNotFoundException or GitException or IOException or UnauthorizedAccessException)
        {
            return Error(e.Message);
        }
    }

    const string NoDescription =
        "No description yet. The FloorPlan prompt describe-project says how to write one; save it with describe.";

    /// <summary>
    /// The project's description in full, checked against a fresh read-only scan: what no longer
    /// matches, what each system holds, the code no system names yet, and where it is kept.
    /// </summary>
    public async Task<BoardToolResult> DescriptionAsync(string? project = null, CancellationToken cancellationToken = default)
    {
        var (root, file, error) = await DescriptionPlaceAsync(project, cancellationToken);
        if (error is not null) return Error(error);
        try
        {
            if (!File.Exists(file)) return Result(new DescriptionReply { Project = root!, File = file!, Note = NoDescription });
            var index = await ScanAsync(root!, cancellationToken);
            return Result(Reply(root!, file!, Descriptions.Load(file!, index), index, saved: null));
        }
        catch (Exception e) when (e is DirectoryNotFoundException or GitException or IOException or UnauthorizedAccessException)
        {
            return Error(e.Message);
        }
    }

    /// <summary>
    /// Checks a description an agent wrote against a fresh read-only scan and, unless it has
    /// problems, saves it where FloorPlan keeps the project's description; open windows show it
    /// at once. Stale anchors are reported, not refused, so they can be fixed in another round.
    /// </summary>
    public async Task<BoardToolResult> DescribeAsync(string description, string? project = null, CancellationToken cancellationToken = default)
    {
        ProjectDescription? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ProjectDescription>(description, ScanJson.Options);
        }
        catch (JsonException e)
        {
            return Error($"Not a description: {e.Message}");
        }
        if (parsed is null) return Error("Not a description: it is empty");
        var (root, file, error) = await DescriptionPlaceAsync(project, cancellationToken);
        if (error is not null) return Error(error);
        try
        {
            var index = await ScanAsync(root!, cancellationToken);
            var report = Descriptions.Check(file!, parsed, index);
            if (report.Problems is { Count: > 0 })
                return Result(Reply(root!, file!, report, index, saved: false) with { Note = "Not saved: fix the problems and call describe again." });
            Directory.CreateDirectory(Path.GetDirectoryName(file!)!);
            var temp = file + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(parsed, ScanJson.Indented), cancellationToken);
            File.Move(temp, file!, overwrite: true);
            var note = report.Stale is null ? "Saved; open windows show it now." : "Saved, with stale anchors: fix them and call describe again.";
            return Result(Reply(root!, file!, report, index, saved: true) with { Note = note });
        }
        catch (Exception e) when (e is DirectoryNotFoundException or GitException or IOException or UnauthorizedAccessException)
        {
            return Error(e.Message);
        }
    }

    /// <summary>The project a description call concerns, chosen as summaries choose it, and its description's file.</summary>
    async Task<(string? Root, string? File, string? Error)> DescriptionPlaceAsync(string? project, CancellationToken cancellationToken)
    {
        if (descriptionPath is null) return (null, null, "This FloorPlan keeps no project descriptions");
        var (_, targets) = await TargetsAsync(project, null, cancellationToken);
        string root;
        if (targets.Count > 0) root = targets[0].Project!;
        else if (ProjectFolder(project) is { } folder) root = await ProjectRootAsync(folder, cancellationToken);
        else return (null, null, $"No open window or folder for the project \"{project}\"");
        return (root, descriptionPath(root), null);
    }

    async Task<ProjectIndex> ScanAsync(string root, CancellationToken cancellationToken) =>
        new(await new ProjectScanner(git).ScanAsync(root, scanOptions(root), null, cancellationToken));

    static DescriptionReply Reply(string root, string file, DescriptionReport report, ProjectIndex index, bool? saved) => new()
    {
        Project = root,
        File = file,
        Saved = saved,
        Description = report.Description,
        Problems = report.Problems,
        Stale = report.Stale,
        Systems = report.Description is null ? null : Descriptions.Systems(report, index),
        Unnamed = report.Unnamed,
    };

    /// <summary>Raises the window showing the project, or starts one and waits until it has scanned.</summary>
    public async Task<BoardToolResult> OpenAsync(string? project = null, CancellationToken cancellationToken = default)
    {
        var open = await OpenWindowsAsync(cancellationToken);
        string root;
        if (Named(open, project) is { } named)
        {
            root = named.Project!;
        }
        else if (ProjectFolder(project) is { } folder)
        {
            root = await ProjectRootAsync(folder, cancellationToken);
        }
        else
        {
            return Error($"No folder for the project \"{project}\"");
        }

        if (open.FirstOrDefault(w => SameFolder(w.Project!, root)) is { } window)
        {
            var (result, error) = await AskAsync(window, new BoardRequest { Op = "raise" }, cancellationToken);
            return result is null ? Error(error!) : Result(new { opened = false, raised = true, project = root });
        }
        if (startWindow is null || !startWindow(root)) return Error($"Could not start a FloorPlan window on {root}");

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < OpenTimeout)
        {
            await Task.Delay(250, cancellationToken);
            if ((await OpenWindowsAsync(cancellationToken)).FirstOrDefault(w => SameFolder(w.Project!, root)) is { } started)
            {
                // Windows may start a process's first window behind the one in use.
                await AskAsync(started, new BoardRequest { Op = "raise" }, cancellationToken);
                return Result(new { opened = true, project = root });
            }
        }
        return Result(new { opened = true, project = root, note = "The window is still scanning; try again in a moment" });
    }

    async Task<List<BoardWindow>> OpenWindowsAsync(CancellationToken cancellationToken) =>
        (await BoardChannel.FindWindowsAsync(cancellationToken)).Where(w => w.Project is not null).ToList();

    /// <summary>
    /// The windows a call is for, and every open window: the named project, else the project an
    /// absolute path points into, else the project of the window the human used last.
    /// </summary>
    async Task<(IReadOnlyList<BoardWindow> All, List<BoardWindow> Targets)> TargetsAsync(
        string? project, IReadOnlyList<string>? paths, CancellationToken cancellationToken)
    {
        var all = await BoardChannel.FindWindowsAsync(cancellationToken);
        var open = all.Where(w => w.Project is not null).ToList();
        var root = ChooseProject(open, project, paths);
        return (all, root is null ? [] : open.Where(w => SameFolder(w.Project!, root)).ToList());
    }

    /// <summary>
    /// The project a call is about, among the open windows (the one used last first): the named
    /// project, else the one an absolute path points into, else the one the human used last.
    /// </summary>
    internal string? ChooseProject(IReadOnlyList<BoardWindow> open, string? project, IReadOnlyList<string>? paths)
    {
        if (!string.IsNullOrWhiteSpace(project))
        {
            var text = project.Trim();
            if (Named(open, text) is { } named) return named.Project;
            // A name that is no window's project must name a real folder, or it would fall into the agent's own project.
            return Deepest(open, Path.IsPathFullyQualified(text) ? FullPath(text) : ProjectFolder(text));
        }
        if (paths?.Select(p => BoardService.SplitLines(p).Path).FirstOrDefault(Path.IsPathFullyQualified) is { } absolute)
            return Deepest(open, absolute);
        return open.FirstOrDefault()?.Project;
    }

    static BoardWindow? Named(IEnumerable<BoardWindow> open, string? project) =>
        string.IsNullOrWhiteSpace(project) ? null : open.FirstOrDefault(w => string.Equals(w.Name, project.Trim(), StringComparison.OrdinalIgnoreCase));

    static string? Deepest(IEnumerable<BoardWindow> open, string? path) =>
        path is null ? null : open.Where(w => Inside(w.Project!, path)).OrderByDescending(w => w.Project!.Length).FirstOrDefault()?.Project;

    string? FullPath(string path)
    {
        try
        {
            return Path.GetFullPath(Path.Combine(Folder, path));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// The folder a project names: absolute, relative to the agent's folder, or a sibling of it,
    /// since repositories often sit side by side. The agent's folder when none is named.
    /// </summary>
    string? ProjectFolder(string? project)
    {
        if (string.IsNullOrWhiteSpace(project)) return Folder;
        var text = project.Trim();
        string[] candidates = Path.IsPathFullyQualified(text)
            ? [text]
            : [Path.Combine(Folder, text), Path.Combine(Path.GetDirectoryName(Folder) ?? Folder, text)];
        foreach (var candidate in candidates)
        {
            try
            {
                var full = Path.GetFullPath(candidate);
                if (Directory.Exists(full)) return Path.TrimEndingDirectorySeparator(full);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }
        return null;
    }

    static string Trim(string path) => Path.TrimEndingDirectorySeparator(path);

    static bool SameFolder(string a, string b) => string.Equals(Trim(a), Trim(b), StringComparison.OrdinalIgnoreCase);

    static bool Inside(string root, string path) =>
        SameFolder(root, path) || Trim(path).StartsWith(Trim(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    async Task<(JsonObject? Result, string? Error)> AskAsync(BoardWindow window, BoardRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var reply = await BoardChannel.RequestAsync(window.Pipe, request, cancellationToken);
            if (reply is null) return (null, $"The FloorPlan window on {window.Project} did not answer");
            if (!reply.Ok || reply.Result is not { } result) return (null, reply.Error ?? "The FloorPlan window refused the request");
            return (JsonNode.Parse(result.GetRawText()) as JsonObject, null);
        }
        catch (Exception e) when ((e is IOException or JsonException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
        {
            return (null, $"The FloorPlan window on {window.Project} did not answer: {e.Message}");
        }
    }

    /// <summary>Says why paths that exist are not on the map: ignored by Git, or untracked.</summary>
    async Task ExplainOffMapAsync(JsonObject report, CancellationToken cancellationToken)
    {
        if (report["notOnMap"] is not JsonArray entries || entries.Count == 0 || report["project"]?.GetValue<string>() is not { } root) return;
        var paths = entries.Select(e => e?["path"]?.GetValue<string>()).OfType<string>().ToList();
        HashSet<string>? ignored = null;
        try
        {
            var result = await git.RunAsync(root, ["check-ignore", "--", .. paths], cancellationToken);
            // Exit 0: some are ignored, 1: none are; anything else: not a repository.
            if (result.ExitCode is 0 or 1) ignored = result.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToHashSet(StringComparer.Ordinal);
        }
        catch (GitUnavailableException)
        {
        }
        foreach (var entry in entries.OfType<JsonObject>())
        {
            var path = entry["path"]?.GetValue<string>();
            entry["reason"] = ignored is null ? "outside the scan" : path is not null && ignored.Contains(path) ? "ignored by Git" : "untracked";
        }
    }

    /// <summary>The repository's top folder, or the folder itself outside a repository.</summary>
    async Task<string> ProjectRootAsync(string folder, CancellationToken cancellationToken)
    {
        try
        {
            var result = await git.RunAsync(folder, ["rev-parse", "--show-toplevel"], cancellationToken);
            if (result.ExitCode == 0 && result.Text.Length > 0) return Trim(Path.GetFullPath(result.Text));
        }
        catch (GitUnavailableException)
        {
        }
        return folder;
    }

    static BoardToolResult NoWindow(string? project, IReadOnlyList<BoardWindow> all, string consequence)
    {
        var open = all.Where(w => w.Project is not null).Select(w => w.Project!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        int older = all.Count(w => w.Project is null && w.Note is not null);
        var showing = string.IsNullOrWhiteSpace(project) ? "this project" : $"\"{project}\"";
        var note = (open.Count == 0 ? "No FloorPlan window is open, " : $"No FloorPlan window shows {showing}, ") + consequence +
                   (older > 0 ? $" {older} FloorPlan window(s) run an older version and need a restart." : "");
        return Result(new { window = false, openWindows = open.Count > 0 ? open : null, note });
    }

    static BoardToolResult Result(object value) => new(JsonSerializer.Serialize(value, ScanJson.Options), false);

    static BoardToolResult Error(string message) => new(JsonSerializer.Serialize(new { error = message }, ScanJson.Options), true);
}

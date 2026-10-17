using System.ComponentModel;
using FloorPlan.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FloorPlan.App;

/// <summary>
/// <c>floorplan mcp [folder]</c>: an MCP server on stdio for a coding agent. One server serves every
/// open FloorPlan window; the folder (by default the current one) is where the agent works, the base
/// of its relative paths and the project used when no window is involved. It only reads projects.
/// </summary>
static class McpCommand
{
    const string ProjectParameter =
        "Which project: a folder name such as \"my-game\", or a path. Omit it for the window the human used last.";

    // What the human sees, so agents can talk about the map in its own terms.
    const string Instructions =
        "FloorPlan shows repositories as maps on the human's screen. A map is a treemap: one rectangle per file, nested in " +
        "its folders, with folder names in header strips. Area is a file's lines in code and documentation (area: lines) or " +
        "its size on disk (area: bytes). Fill shows activity or kind: with activity, orange marks commits in the human's " +
        "history range, stronger with more (darker in the light theme, brighter in the dark one), and grey means none; with " +
        "kind, blue is code, orange documentation, green assets, grey data and other files. Violet outlines mark uncommitted " +
        "changes; raspberry outlines are agents' pointers, with their captions in the row under the map. The strip along the " +
        "bottom is the commit history, and its highlighted range is what activity counts. The reader beside the map shows a " +
        "file or a diff.\n\n" +
        "When the human says \"this\", \"here\" or \"these\", call get_selection. If it reports stale, the window has been " +
        "idle for a while: confirm what they mean. In a diff, line numbers count in the version named by lineNumbers and " +
        "removed lines have none; rely on selectedText. Instead of listing files in prose, call show (\"path:480-492\" points " +
        "at lines); at the end of a task, show the files you changed. Every tool takes an optional project and otherwise " +
        "uses the window the human used last, so check the project in each reply. Call open only when the human wants to " +
        "see something that no window shows.";

    public static int Run(string[] args)
    {
        var folder = args.FirstOrDefault(a => !a.StartsWith('-')) ?? Environment.CurrentDirectory;
        if (!Directory.Exists(folder))
        {
            NativeMethods.AttachParentConsoleIfNeeded();
            Console.Error.WriteLine($"Folder not found: {folder}");
            return 2;
        }
        var settings = AppData.LoadSettings();
        var tools = new BoardTools(
            folder,
            new GitRunner(),
            root => new ScanOptions { IncludeUntracked = settings.For(root).IncludeUntracked },
            root => Environment.ProcessPath is { } exe && NativeMethods.StartDetached(exe, [root], root));

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "floorplan", Title = "FloorPlan", Version = typeof(McpCommand).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" },
            ServerInstructions = Instructions,
            ToolCollection =
            [
                McpServerTool.Create(
                    async ([Description(ProjectParameter)] string? project = null, CancellationToken cancellationToken = default) =>
                        Result(await tools.SelectionAsync(project, cancellationToken)),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_selection",
                        Title = "What the human is looking at",
                        Description =
                            "What the human has selected on a FloorPlan map: the file or folder (with its measurements, when it was " +
                            "selected and, if HEAD moved since, the HEAD at that time), the file in the reader with the lines in view and " +
                            "any selected lines and text, the zoomed folder, the commit selected on the history strip, the history range, " +
                            "the last few selections, how long the window has been idle, and the other open windows. Use it to resolve " +
                            "\"this\" or \"here\".",
                        ReadOnly = true,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
                McpServerTool.Create(
                    async (
                        [Description("Files or folders to outline: paths relative to the project root or your working folder, or absolute. Add \":480-492\" to a file to point at those lines.")] string[] paths,
                        [Description("One short line the human reads under the map, e.g. \"Where saving reads the player's state\". Empty paths clear your pointer.")] string caption,
                        McpServer server,
                        [Description("Your name as the human should see it, when several agents share the board, e.g. \"Claude (save system)\".")] string? agent = null,
                        [Description(ProjectParameter)] string? project = null,
                        [Description("Also bring it into view: raise the window, zoom the map to the paths and open the reader at the lines. Use it when the human asked to be shown.")] bool reveal = false,
                        CancellationToken cancellationToken = default) =>
                        Result(await tools.ShowAsync(paths, caption, agent ?? server.ClientInfo?.Title ?? server.ClientInfo?.Name, project, reveal, cancellationToken)),
                    new McpServerToolCreateOptions
                    {
                        Name = "show",
                        Title = "Point at files on the map",
                        Description =
                            "Outlines files and folders on the human's FloorPlan map with a caption, instead of listing them in prose; " +
                            "every window on the project draws it. The pointer stays until you point again or the human dismisses it. " +
                            "Reports which paths were shown, which exist but are not on the map (ignored or untracked; their nearest " +
                            "folder is outlined instead), which are not in the project, and whether any window is open at all; if none " +
                            "is, describe the files in text.",
                        ReadOnly = false,
                        Destructive = false,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
                McpServerTool.Create(
                    async (
                        [Description("A folder or file, relative to the project root or your working folder; omit for the whole project.")] string? path = null,
                        [Description(ProjectParameter)] string? project = null,
                        CancellationToken cancellationToken = default) =>
                        Result(await tools.SummaryAsync(path, project, cancellationToken)),
                    new McpServerToolCreateOptions
                    {
                        Name = "summary",
                        Title = "Size and activity of a folder",
                        Description =
                            "Footprint and activity of a folder or file from the model the human sees: files, lines (a folder's " +
                            "codeAndDocLines; composition adds data), bytes, commits in the human's history range, uncommitted files, " +
                            "the largest parts and recent commits. Without an open window it scans the project and counts the latest " +
                            "40 commits.",
                        ReadOnly = true,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
                McpServerTool.Create(
                    async (
                        [Description("Which project: a folder name such as \"my-game\" (also found beside your working folder), or a path. Omit it for your working folder's project.")] string? project = null,
                        CancellationToken cancellationToken = default) =>
                        Result(await tools.OpenAsync(project, cancellationToken)),
                    new McpServerToolCreateOptions
                    {
                        Name = "open",
                        Title = "Open the map",
                        Description =
                            "Brings the FloorPlan window showing a project above other windows, without taking the keyboard from the " +
                            "human, or opens one and waits until it has scanned. Only when the human wants to see something.",
                        ReadOnly = false,
                        Destructive = false,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
            ],
        };

        RunAsync(options).GetAwaiter().GetResult();
        return 0;
    }

    static async Task RunAsync(McpServerOptions options)
    {
        await using var transport = new StdioServerTransport(options);
        await using var server = McpServer.Create(transport, options);
        await server.RunAsync();
    }

    static CallToolResult Result(BoardToolResult result) => new()
    {
        Content = [new TextContentBlock { Text = result.Json }],
        IsError = result.IsError,
    };
}

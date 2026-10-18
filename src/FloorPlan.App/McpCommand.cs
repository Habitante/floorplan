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
        "its folders, with folder names in header strips; wider gutters and larger headers mark the top-level regions. Area " +
        "is a file's lines in code, tests and documentation (area: lines) or its size on disk (area: bytes). Fill shows " +
        "activity, age or kind. Activity and age share one warm ramp from red through orange to yellow, where more is " +
        "brighter in the dark theme and darker in the light one: activity counts commits in the human's history range, and " +
        "age is how long before the range's newest commit a file was last committed (strongest within a day of it, faintest " +
        "within a year). Files with no commits in the range, or none within a year, keep a dim tint of their kind; under age, hatching " +
        "marks a file whose last commit the loaded history cannot tell (it came later, was never committed, or is older than " +
        "the history FloorPlan read). Replies give each file and folder's lastCommit, as age shows it. With kind, blue is " +
        "code, teal tests, green assets, and documentation the grey that contrasts most with the background; data and other " +
        "files are the muted grey. Violet outlines mark uncommitted changes, with a filled corner on new files (renamed ones " +
        "count as new), and region headers count new, modified and deleted files in violet (+2 ~1 −1); raspberry outlines " +
        "are agents' pointers, with their captions in the row under the map, which shows a caption's start; clicking it opens " +
        "the whole note with its paths. A selected file has a strong ink outline, and fainter ink outlines mark the files " +
        "that change with it (summary gives them as changesWith). The strip along the bottom is the commit history, and its highlighted " +
        "range is what activity and age count; a chip in the row above it says what landed since the human was last there " +
        "and, when clicked, switches the range to those commits. The reader beside the map shows a file or a diff.\n\n" +
        "When the human says \"this\", \"here\" or \"these\", call get_selection. If it reports stale, the human has not " +
        "touched the window for a while: confirm what they mean. sinceLastSeen in a reply is what landed while the human " +
        "was away: commits since the HEAD they last saw, the regions those touched, and who made them (agents appear as " +
        "co-authors). If the window showed it in the last few minutes, or no window is open, tell them in a line or two " +
        "before anything else. In a diff, line numbers count in the version named by lineNumbers and " +
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
        // Settings are read for each call: windows change them while the server runs.
        var tools = new BoardTools(
            folder,
            new GitRunner(),
            root => new ScanOptions { IncludeUntracked = !AppData.LoadSettings().For(root).HideUntracked },
            root => Environment.ProcessPath is { } exe && NativeMethods.StartDetached(exe, [root], root),
            root => AppData.LoadSettings().Seen(root));

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
                            "What the human has selected on a FloorPlan map: the file or folder (with its measurements and last commit, " +
                            "when it was selected and, if HEAD moved since, the HEAD at that time), the file in the reader with the lines in view and " +
                            "any selected lines and text, the zoomed folder, the commit selected on the history strip, the history range, " +
                            "the last few selections, how long the human has left the window idle, the other open windows, and what " +
                            "landed while the human was away when the window shows that. Use it to resolve \"this\" or \"here\".",
                        ReadOnly = true,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
                McpServerTool.Create(
                    async (
                        [Description("Files or folders to outline: paths relative to the project root or your working folder, or absolute. Add \":480-492\" to a file to point at those lines.")] string[] paths,
                        [Description("What the human reads under the map, e.g. \"Where saving reads the player's state\". The row shows its start and a click opens the rest, so lead with the point. Empty paths clear your pointer.")] string caption,
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
                            "codeAndDocLines; composition adds data), bytes, commits in the human's history range, the last commit, uncommitted files, " +
                            "the largest parts and recent commits; for the whole project, also what landed since the human last saw it. " +
                            "For a file, also the files that change in the same commits over the loaded history (changesWith: together " +
                            "in how many of its commitsLoaded, strength 0 to 1, commits touching many files counting for less) and its " +
                            "tests or code by naming convention (pairedByName). Changing together is not depending, and pairing is not " +
                            "coverage. Without an open window it scans the project and counts the latest 40 commits.",
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

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

    // How to use the board, and what the human sees, so agents can talk about the map in its own terms.
    const string Instructions =
        "FloorPlan is a visual window onto the repository that you share with the human: a map of its files on their screen, " +
        "which you can read through these tools and point at. Use it whenever you discuss the repository's shape, status, " +
        "contents or changes, without being asked: point at what you mean with show instead of listing files in prose, and if " +
        "no window shows the project, open it (it does not take the keyboard) and tell the human to have a look.\n\n" +
        "A map is a treemap: one rectangle per file, nested in " +
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
        "that change with it (summary gives them as changesWith). When the project has a description, fill can also be systems: " +
        "a colour per system, with its code of one to three letters on the regions it holds and on files that belong to another " +
        "system than their folder. The strip along the bottom is the commit history, and its highlighted " +
        "range is what activity and age count; a chip in the row above it says what landed since the human was last there " +
        "and, when clicked, switches the range to those commits. Double-clicking a file zooms the map into it: its parts " +
        "(classes, functions, sections, and blocks of a kind of data declaration) as rectangles sized by lines, with the same " +
        "fills and outlines, where under kind code is blue and data grey; get_selection then reports the file as the zoom. " +
        "The reader beside the map shows a file or a diff. A file opens " +
        "as its skeleton: declarations, data and comments in view, bodies folded to a chip with their size, and a document " +
        "folded to its headings; lines you point at open by themselves. A violet bar in its margin marks lines changed since " +
        "the last commit, a wedge where lines were deleted, and a folded body shows its changes as +added −removed.\n\n" +
        "When the human says \"this\", \"here\" or \"these\", call get_selection. If it reports stale, the human has not " +
        "touched the window for a while: confirm what they mean. sinceLastSeen in a reply is what landed while the human " +
        "was away: commits since the HEAD they last saw, the regions those touched, and who made them (agents appear as " +
        "co-authors). If the window showed it in the last few minutes, or no window is open, tell them in a line or two " +
        "before anything else. In a diff, line numbers count in the version named by lineNumbers and " +
        "removed lines have none; rely on selectedText. When the human picked a part of a file, or selected lines inside a " +
        "function, get_selection names it (part, reader.symbol), so \"this\" can be a function. show points at files, folders, " +
        "lines (\"path:480-492\") and symbols by the ids a file's summary outline gives (\"path#Class.method\"). At the end of a task, show the files " +
        "you changed. Every tool takes an optional project and otherwise uses the window the " +
        "human used last, so check the project in each reply.\n\n" +
        "A project description says what the project's parts are for and where they live: its systems with the paths they " +
        "own, where the project is heading, and findings worth knowing. Agents write it, FloorPlan checks it against the " +
        "files, and the human sees it as the systems fill and in the side panel. Read it at the start of work on a project " +
        "and before you explain where something is or how parts relate: a project's summary carries it in brief, " +
        "get_description in full. Keep it current without being asked: when there is none, when summary reports it stale " +
        "or with code in no system, and after any task of yours that added, moved or removed a part's files, refresh it " +
        "with describe, following the describe-project prompt. Start from get_description, keep its ids, change what " +
        "changed, and say so when you hand over. Rewrite one wholesale only when the human asks.";

    /// <summary>The describe-project prompt, embedded from Prompts/describe-project.md.</summary>
    static readonly Lazy<string> DescribePrompt = new(() =>
    {
        using var stream = typeof(McpCommand).Assembly.GetManifestResourceStream("prompts/describe-project.md")
            ?? throw new InvalidOperationException("The describe-project prompt is not embedded");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

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
            root => AppData.LoadSettings().Seen(root),
            AppData.DescriptionPath);

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
                            "when it was selected and, if HEAD moved since, the HEAD at that time) and the part of a file they picked on the " +
                            "map inside it (part: its id, kind and lines), the file in the reader with the lines in view, any selected lines and " +
                            "text, and the declaration or section around those lines (symbol), the zoom, the commit selected on the history strip, the history range, " +
                            "the last few selections, how long the human has left the window idle, the other open windows, and what " +
                            "landed while the human was away when the window shows that. Use it to resolve \"this\" or \"here\".",
                        ReadOnly = true,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
                McpServerTool.Create(
                    async (
                        [Description("Files or folders to outline: paths relative to the project root or your working folder, or absolute. Add \":480-492\" to a file to point at those lines, or \"#Class.method\" to point at a declaration or section by the id its file's summary outline gives.")] string[] paths,
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
                            "folder is outlined instead), which are not in the project or name a symbol their file does not declare, each " +
                            "symbol with the lines it was shown as, and whether any window is open at all; if none is, describe the files in text.",
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
                            "coverage. For a source file or a document, also its outline: the symbols it declares (classes, functions, data) " +
                            "or its sections, in file order, each with its lines and an id unique in the file. Without an open window it " +
                            "scans the project and counts the latest 40 commits.",
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
                            "human, or opens one and waits until it has scanned. Use it when you discuss the repository and no window " +
                            "shows the project, then tell the human to look; while one is open, point with show instead.",
                        ReadOnly = false,
                        Destructive = false,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
                McpServerTool.Create(
                    async ([Description(ProjectParameter)] string? project = null, CancellationToken cancellationToken = default) =>
                        Result(await tools.DescriptionAsync(project, cancellationToken)),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_description",
                        Title = "The project's description",
                        Description =
                            "The project's description in full: its systems with the paths they own, its direction and findings, checked " +
                            "against a fresh read-only scan. Also reports what no longer matches (stale), what each system holds, the " +
                            "largest code no system names (unnamed), and where the description is kept, outside the project. Read it " +
                            "before updating a description, or for a full briefing; a project's summary carries it in brief.",
                        ReadOnly = true,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
                McpServerTool.Create(
                    async (
                        [Description("The whole description as a JSON string, format 1: floorplan, project, written, summary, systems, direction, findings. The describe-project prompt explains each field.")] string description,
                        [Description(ProjectParameter)] string? project = null,
                        CancellationToken cancellationToken = default) =>
                        Result(await tools.DescribeAsync(description, project, cancellationToken)),
                    new McpServerToolCreateOptions
                    {
                        Name = "describe",
                        Title = "Save the project's description",
                        Description =
                            "Checks a project description you wrote against a fresh read-only scan and saves it in FloorPlan's own data, " +
                            "never in the project; open windows show it at once. Replies with problems (then nothing is saved), stale " +
                            "anchors, what each system holds and the largest code no system names. Replaces the previous description, so " +
                            "start from get_description. The describe-project prompt says how to write one. Use it on your own whenever " +
                            "the description is missing or out of date, as after a task that added, moved or removed a part's files.",
                        ReadOnly = false,
                        Destructive = false,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
            ],
            PromptCollection =
            [
                McpServerPrompt.Create(
                    () => DescribePrompt.Value,
                    new McpServerPromptCreateOptions
                    {
                        Name = "describe-project",
                        Title = "Describe this project for FloorPlan",
                        Description = "How to write or refresh the project description that FloorPlan draws as named systems on its map.",
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

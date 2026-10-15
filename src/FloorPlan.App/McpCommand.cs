using System.ComponentModel;
using FloorPlan.Core;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FloorPlan.App;

/// <summary>
/// <c>floorplan mcp [folder]</c>: an MCP server on stdio for the coding agent working in the folder
/// (by default the current one). It reaches the window showing that project over the board
/// channel, and only reads the project.
/// </summary>
static class McpCommand
{
    const string Instructions =
        "FloorPlan shows this repository as a map on the human's screen. When the human says \"this\", \"here\" or " +
        "\"these\", call get_selection to see what they selected or are reading. Instead of listing files in prose, call " +
        "show to outline them on the map with a short caption: the files a planned change will touch, or at the end of a " +
        "task, the files you changed. summary gives a folder's size, composition and recent activity as the human sees it.";

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
        var tools = new BoardTools(folder, new GitRunner(), root => new ScanOptions { IncludeUntracked = settings.For(root).IncludeUntracked });

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "floorplan", Title = "FloorPlan", Version = typeof(McpCommand).Assembly.GetName().Version?.ToString(3) ?? "1.0.0" },
            ServerInstructions = Instructions,
            ToolCollection =
            [
                McpServerTool.Create(
                    async (CancellationToken cancellationToken) => Result(await tools.SelectionAsync(cancellationToken)),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_selection",
                        Title = "What the human is looking at",
                        Description =
                            "What the human has selected on the FloorPlan map of this project: the file or folder (with its measurements, " +
                            "when it was selected and, if HEAD moved since, the HEAD at that time), the file open in the reader with the " +
                            "lines in view and any lines selected, the folder the map is zoomed into, the commit selected on the history " +
                            "strip, the history range, and the last few selections. Use it to resolve \"this\" or \"here\".",
                        ReadOnly = true,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
                McpServerTool.Create(
                    async (
                        [Description("Files or folders to outline: paths relative to the project root or your working folder, or absolute.")] string[] paths,
                        [Description("One short line the human reads next to the outlines, e.g. \"Where saving reads the player's state\". Empty paths clear your pointer.")] string caption,
                        McpServer server,
                        [Description("Your name as the human should see it, when several agents share the project, e.g. \"Claude (save system)\".")] string? agent = null,
                        CancellationToken cancellationToken = default) =>
                        Result(await tools.ShowAsync(paths, caption, agent ?? server.ClientInfo?.Title ?? server.ClientInfo?.Name, cancellationToken)),
                    new McpServerToolCreateOptions
                    {
                        Name = "show",
                        Title = "Point at files on the map",
                        Description =
                            "Outlines files and folders on the human's FloorPlan map with a caption, instead of listing them in prose. The " +
                            "pointer stays until you point again or the human dismisses it. Reports which paths were shown, which are not in " +
                            "the project, and whether a window is open at all; if not, describe the files in text.",
                        ReadOnly = false,
                        Destructive = false,
                        Idempotent = true,
                        OpenWorld = false,
                    }),
                McpServerTool.Create(
                    async (
                        [Description("A folder or file, relative to the project root or your working folder; omit for the whole project.")] string? path = null,
                        CancellationToken cancellationToken = default) =>
                        Result(await tools.SummaryAsync(path, cancellationToken)),
                    new McpServerToolCreateOptions
                    {
                        Name = "summary",
                        Title = "Size and activity of a folder",
                        Description =
                            "Footprint and activity of a folder or file from the model the human sees: files, lines (code and documentation), " +
                            "bytes, composition by kind, commits in the human's history range, uncommitted files, the largest parts and recent " +
                            "commits. Without an open window it scans the project and counts the latest 40 commits.",
                        ReadOnly = true,
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

using System.Text.Json;

namespace FloorPlan.Core.Tests;

sealed class FakeSurface(ProjectIndex? index) : IBoardSurface
{
    public ProjectIndex? Index { get; } = index;
    public BoardState State { get; } = new();
    public bool Focused { get; set; }
    public List<Pointer> Pointers { get; } = [];
    public bool Revealed { get; private set; }
    public int Raised { get; private set; }

    public void Point(Pointer pointer, bool reveal)
    {
        Pointers.Add(pointer);
        Revealed |= reveal;
    }

    public void Raise() => Raised++;
}

public class BoardServiceTests
{
    static readonly string Root = Path.Combine(Path.GetTempPath(), "floorplan-service", "proj");
    static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    static FakeSurface Surface() => new(ProjectIndexTests.Index(Root, "scenes/world.gd", "scenes/holding.gd", "core/vm.gd"));

    static T Result<T>(BoardReply reply) => reply.Result!.Value.Deserialize<T>(ScanJson.Options)!;

    static BoardReply Show(FakeSurface surface, string? caption, params string[] paths) =>
        BoardService.Handle(surface, new BoardRequest { Op = "show", Paths = paths, Caption = caption, Agent = "Claude", Base = Root }, Now);

    [Fact]
    public void Show_points_at_known_paths_and_reports_the_rest()
    {
        var surface = Surface();
        var report = Result<ShowReport>(Show(surface, " Where the\n save \u0007 goes ",
            "scenes/world.gd", @"scenes\world.gd", Path.Combine(Root, "core"), "nope.gd", ".", "../outside.txt"));

        Assert.True(report.Window);
        Assert.Equal(Root, report.Project);
        Assert.Equal(["scenes/world.gd", "core"], report.Shown);
        Assert.Equal(["nope.gd", ".", "../outside.txt"], report.Unknown);
        var pointer = Assert.Single(surface.Pointers);
        Assert.Equal("Claude", pointer.Agent);
        Assert.Equal("Where the save goes", pointer.Caption);
        Assert.Equal([new PointerItem("scenes/world.gd"), new PointerItem("core")], pointer.Items);
        Assert.False(surface.Revealed);
        Assert.Equal(0, surface.Raised);
    }

    [Fact]
    public void Show_points_at_lines_of_a_file()
    {
        var surface = Surface();
        var report = Result<ShowReport>(Show(surface, "two functions",
            "scenes/world.gd:3-5", "scenes/world.gd#L9", Path.Combine(Root, "core", "vm.gd") + ":12", "scenes:4"));

        Assert.Equal(["scenes/world.gd:3-5", "scenes/world.gd:9", "core/vm.gd:12", "scenes"], report.Shown);
        Assert.Equal(new PointerItem("scenes/world.gd", 3, 5), surface.Pointers[0].Items[0]);
    }

    [Fact]
    public void Splits_line_ranges_but_not_drive_letters()
    {
        Assert.Equal(("a/b.gd", 480, 492), BoardService.SplitLines("a/b.gd:480-492"));
        Assert.Equal(("a/b.gd", 7, 7), BoardService.SplitLines(" a/b.gd:7 "));
        Assert.Equal(("a/b.gd", 3, 9), BoardService.SplitLines("a/b.gd#L3-L9"));
        Assert.Equal((@"C:\src\b.gd", 2, 4), BoardService.SplitLines(@"C:\src\b.gd:2-4"));
        Assert.Equal((@"C:\src\b.gd", (int?)null, (int?)null), BoardService.SplitLines(@"C:\src\b.gd"));
        Assert.Equal(("C:5", (int?)null, (int?)null), BoardService.SplitLines("C:5"));
        Assert.Equal(("a/b.gd:9-3", (int?)null, (int?)null), BoardService.SplitLines("a/b.gd:9-3"));
    }

    [Fact]
    public void Reveal_brings_the_pointer_into_view_and_the_window_forward()
    {
        var surface = Surface();
        BoardService.Handle(surface, new BoardRequest { Op = "show", Paths = ["core/vm.gd"], Caption = "here", Reveal = true }, Now);

        Assert.True(surface.Revealed);
        Assert.Equal(1, surface.Raised);
    }

    [Fact]
    public async Task Paths_that_exist_off_the_map_outline_their_nearest_folder()
    {
        using var repo = new TempRepo().Write(".gitignore", "renders/\n").Write("tools/blender/room.py", "x\n").Commit("Start");
        repo.Write("tools/blender/renders/frame 1.png", "png");
        var surface = new FakeSurface(new ProjectIndex(await new ProjectScanner(new GitRunner()).ScanAsync(repo.Root)));

        var report = Result<ShowReport>(BoardService.Handle(surface,
            new BoardRequest { Op = "show", Paths = ["tools/blender/renders/frame 1.png", "tools/blender/renders/missing.png"], Caption = "renders" }, Now));

        var offMap = Assert.Single(report.NotOnMap!);
        Assert.Equal(("tools/blender/renders/frame 1.png", "tools/blender"), (offMap.Path, offMap.ShownAs));
        Assert.Equal(["tools/blender/renders/missing.png"], report.Unknown);
        Assert.Empty(report.Shown);
        Assert.Equal(new PointerItem("tools/blender", Note: "tools/blender/renders/frame 1.png"), Assert.Single(Assert.Single(surface.Pointers).Items));
    }

    [Fact]
    public void An_empty_show_clears_and_unknown_paths_change_nothing()
    {
        var surface = Surface();

        var unknown = Result<ShowReport>(Show(surface, "x", "nope.gd"));
        Assert.Empty(surface.Pointers);
        Assert.Empty(unknown.Shown);
        Assert.NotNull(unknown.Note);

        var cleared = Result<ShowReport>(Show(surface, null));
        Assert.Empty(Assert.Single(surface.Pointers).Items);
        Assert.Equal("Pointer cleared", cleared.Note);

        Assert.False(Show(surface, null, Enumerable.Repeat("core/vm.gd", BoardService.MaxPaths + 1).ToArray()).Ok);
    }

    [Fact]
    public void Says_hello_and_raises_even_before_a_scan()
    {
        var empty = new FakeSurface(null) { Focused = true };
        var hello = Result<HelloReport>(BoardService.Handle(empty, new BoardRequest { Op = "hello" }, DateTimeOffset.Now));
        Assert.Null(hello.Project);
        Assert.True(hello.Focused);
        Assert.True(BoardService.Handle(empty, new BoardRequest { Op = "raise" }, Now).Ok);
        Assert.Equal(1, empty.Raised);

        var surface = Surface();
        surface.State.Touch(Now.AddSeconds(-90));
        var named = Result<HelloReport>(BoardService.Handle(surface, new BoardRequest { Op = "hello" }, Now));
        Assert.Equal((Root, "proj", 90L), (named.Project, named.Name, named.IdleSeconds));
    }

    [Fact]
    public void Refuses_before_a_scan_and_unknown_requests()
    {
        Assert.False(BoardService.Handle(new FakeSurface(null), new BoardRequest { Op = "selection" }, Now).Ok);
        Assert.False(BoardService.Handle(Surface(), new BoardRequest { Op = "delete" }, Now).Ok);
        Assert.False(BoardService.Handle(Surface(), new BoardRequest { Op = "summary", Path = "nope" }, Now).Ok);
    }

    [Fact]
    public void Summary_uses_the_humans_area()
    {
        var surface = Surface();
        surface.State.Update(new BoardView { Area = "bytes" }, null, Now);
        var report = Result<SummaryReport>(BoardService.Handle(surface, new BoardRequest { Op = "summary", Path = "scenes" }, Now));

        Assert.True(report.Window);
        Assert.Equal(2, report.Files);
        Assert.Equal(2, report.CodeAndDocLines);
        Assert.Null(report.Lines);
        Assert.Equal(["scenes/holding.gd", "scenes/world.gd"], report.Largest!.Select(n => n.Path));
    }

    [Fact]
    public void A_summary_of_the_whole_project_carries_the_windows_briefing()
    {
        var surface = Surface();
        surface.State.SinceLastSeen = new SinceReport { SeenHead = "abc", SeenAt = Now.AddHours(-3), Commits = 4, ShownAt = Now.AddSeconds(-20) };

        var whole = Result<SummaryReport>(BoardService.Handle(surface, new BoardRequest { Op = "summary" }, Now));
        var part = Result<SummaryReport>(BoardService.Handle(surface, new BoardRequest { Op = "summary", Path = "scenes" }, Now));

        Assert.Equal((4, 20L), (whole.SinceLastSeen!.Commits, whole.SinceLastSeen.SecondsAgo!.Value));
        Assert.Null(part.SinceLastSeen);
    }

    [Fact]
    public void Clean_keeps_one_short_line()
    {
        Assert.Equal("a b c", BoardService.Clean("  a\r\n b\t\tc \u0000", 20));
        Assert.Equal("", BoardService.Clean(" \n ", 20));
        Assert.Equal("abcdefghi…", BoardService.Clean("abcdefghijklmnop", 10));
    }
}

[Collection("Board pipes")]
public class BoardToolsTests
{
    static T Read<T>(BoardToolResult result) => JsonSerializer.Deserialize<T>(result.Json, ScanJson.Options)!;

    static BoardTools Tools(string folder, Func<string, bool>? start = null) =>
        new(folder, new GitRunner(), _ => new ScanOptions(), start);

    /// <summary>A window on the project: a board server answered by a surface over a real scan.</summary>
    sealed class Window : IAsyncDisposable
    {
        readonly BoardServer server;
        public FakeSurface Surface { get; }

        Window(FakeSurface surface)
        {
            Surface = surface;
            server = new BoardServer(BoardChannelTests.UniquePipe(), r => Task.FromResult(BoardService.Handle(surface, r, DateTimeOffset.Now)), _ => { });
        }

        public static async Task<Window> OpenAsync(string root, TimeSpan idle)
        {
            var window = new Window(new FakeSurface(new ProjectIndex(await new ProjectScanner(new GitRunner()).ScanAsync(root))));
            window.Surface.State.Touch(DateTimeOffset.Now - idle);
            await BoardChannelTests.WaitUntil(() => window.server.Serving);
            return window;
        }

        public ValueTask DisposeAsync() => server.DisposeAsync();
    }

    [Fact]
    public void Calls_go_to_the_named_project_else_an_absolute_path_else_the_window_used_last()
    {
        var src = Path.Combine(Path.GetTempPath(), "floorplan-choose", "src");
        string game = Path.Combine(src, "my-game"), house = Path.Combine(src, "pine-trees");
        // Used last first, as discovery orders them.
        BoardWindow[] open = [new("p1", game, "my-game", 5, false), new("p2", house, "pine-trees", 600, false)];
        var tools = Tools(house);

        Assert.Equal(game, tools.ChooseProject(open, null, null));
        Assert.Equal(house, tools.ChooseProject(open, "PINE-TREES", null));
        Assert.Equal(house, tools.ChooseProject(open, house + Path.DirectorySeparatorChar, null));
        Assert.Equal(house, tools.ChooseProject(open, null, [Path.Combine(house, "harness", "a.py") + ":3"]));
        Assert.Equal(game, tools.ChooseProject(open, null, ["harness/a.py"]));
        Assert.Null(tools.ChooseProject(open, "other", null));
        Assert.Null(tools.ChooseProject([], null, null));
    }

    [Fact]
    public async Task Without_a_window_nobody_is_looking_and_summaries_come_from_a_scan()
    {
        using var repo = new TempRepo().Write("scenes/world.gd", "1\n2\n").Write("docs/a.md", "x\n").Commit("Start");
        var tools = Tools(Path.Combine(repo.Root, "scenes"));

        var selection = JsonDocument.Parse((await tools.SelectionAsync(repo.Root)).Json).RootElement;
        var show = await tools.ShowAsync(["world.gd"], "here", "Claude", repo.Root);
        var summary = Read<SummaryReport>(await tools.SummaryAsync("world.gd", repo.Root));

        Assert.False(selection.GetProperty("window").GetBoolean());
        Assert.False(show.IsError);
        Assert.False(Read<ShowReport>(show).Window);
        Assert.False(summary.Window);
        Assert.Equal("scenes/world.gd", summary.Path);     // scanned from the repository's top folder
        Assert.Equal(2, summary.Lines);
        Assert.Equal(1, summary.CommitsInRange);
        Assert.True((await tools.SummaryAsync("nope.gd", repo.Root)).IsError);
    }

    [Fact]
    public async Task Without_a_window_the_project_summary_says_what_landed_since_the_human_last_looked()
    {
        using var repo = new TempRepo().Write("a.gd", "1\n").Commit("Start");
        var seen = repo.Git("rev-parse", "HEAD").Trim();
        repo.Write("a.gd", "2\n").Commit("Since");
        var tools = new BoardTools(repo.Root, new GitRunner(), _ => new ScanOptions(), seen: _ => new SeenMark(seen, DateTimeOffset.Now.AddDays(-1)));

        var whole = Read<SummaryReport>(await tools.SummaryAsync(null, repo.Root));
        var file = Read<SummaryReport>(await tools.SummaryAsync("a.gd", repo.Root));

        Assert.False(whole.Window);
        Assert.Equal(1, whole.SinceLastSeen!.Commits);
        Assert.Equal([new Tally("", 1)], whole.SinceLastSeen.Regions!);
        Assert.Null(file.SinceLastSeen);
    }

    [Fact]
    public async Task A_project_can_be_named_from_a_folder_beside_it()
    {
        using var repo = new TempRepo().Write("a.gd", "1\n").Commit("Start");
        var house = Path.Combine(Path.GetDirectoryName(repo.Root)!, "house-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(house);
        try
        {
            var summary = Read<SummaryReport>(await Tools(house).SummaryAsync(null, Path.GetFileName(repo.Root)));
            Assert.Equal(repo.Root, summary.Project, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(1, summary.Files);
        }
        finally
        {
            Directory.Delete(house);
        }
    }

    [Fact]
    public async Task Every_window_on_the_project_draws_the_pointer_and_the_one_used_last_answers()
    {
        using var repo = new TempRepo().Write(".gitignore", "renders/\n").Write("scenes/world.gd", "1\n").Commit("Start");
        repo.Write("renders/frame.png", "png");
        await using var recent = await Window.OpenAsync(repo.Root, TimeSpan.FromSeconds(5));
        await using var older = await Window.OpenAsync(repo.Root, TimeSpan.FromMinutes(30));
        recent.Surface.State.Update(new BoardView { Selected = "scenes/world.gd" }, null, DateTimeOffset.Now);
        var tools = Tools(repo.Root);

        var show = Read<ShowReport>(await tools.ShowAsync(["scenes/world.gd:1", "renders/frame.png", "nope.gd"], "The world", "Claude", repo.Root, reveal: true));
        var selection = JsonDocument.Parse((await tools.SelectionAsync(repo.Root)).Json).RootElement;
        var summary = Read<SummaryReport>(await tools.SummaryAsync("scenes", repo.Root));

        Assert.True(show.Window);
        Assert.Equal(2, show.Windows);
        Assert.Equal(["scenes/world.gd:1"], show.Shown);
        Assert.Equal(["nope.gd"], show.Unknown);
        var offMap = Assert.Single(show.NotOnMap!);
        Assert.Equal(("renders/frame.png", null, "ignored by Git"), (offMap.Path, offMap.ShownAs, offMap.Reason));
        Assert.Equal("The world", Assert.Single(recent.Surface.Pointers).Caption);
        Assert.Single(older.Surface.Pointers);
        Assert.True(recent.Surface.Revealed);
        Assert.False(older.Surface.Revealed);
        Assert.Equal("scenes/world.gd", selection.GetProperty("selected").GetProperty("path").GetString());
        Assert.False(selection.TryGetProperty("stale", out _));
        Assert.True(summary.Window);
        Assert.True((await tools.SummaryAsync("nope", repo.Root)).IsError);
    }

    [Fact]
    public async Task Open_raises_the_window_or_starts_one_and_waits_for_it()
    {
        using var repo = new TempRepo().Write("a.gd", "1\n").Commit("Start");
        Task<Window>? starting = null;
        var tools = Tools(repo.Root, root =>
        {
            starting = Window.OpenAsync(root, TimeSpan.Zero);
            return true;
        });
        Window? started = null;
        try
        {
            var opened = JsonDocument.Parse((await tools.OpenAsync(repo.Root)).Json).RootElement;
            started = await starting!;
            Assert.True(opened.GetProperty("opened").GetBoolean());
            Assert.Equal(1, started.Surface.Raised);

            var again = JsonDocument.Parse((await tools.OpenAsync(Path.GetFileName(repo.Root))).Json).RootElement;
            Assert.False(again.GetProperty("opened").GetBoolean());
            Assert.True(again.GetProperty("raised").GetBoolean());
            Assert.Equal(2, started.Surface.Raised);
        }
        finally
        {
            if (started is not null) await started.DisposeAsync();
        }
    }
}

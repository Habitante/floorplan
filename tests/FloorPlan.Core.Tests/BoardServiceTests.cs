using System.Text.Json;

namespace FloorPlan.Core.Tests;

sealed class FakeSurface(ProjectIndex? index) : IBoardSurface
{
    public ProjectIndex? Index { get; } = index;
    public BoardState State { get; } = new();
    public List<Pointer> Pointers { get; } = [];
    public void Point(Pointer pointer) => Pointers.Add(pointer);
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
        Assert.Equal(["scenes/world.gd", "core"], report.Shown);
        Assert.Equal(["nope.gd", ".", "../outside.txt"], report.Unknown);
        var pointer = Assert.Single(surface.Pointers);
        Assert.Equal("Claude", pointer.Agent);
        Assert.Equal("Where the save goes", pointer.Caption);
        Assert.Equal(["scenes/world.gd", "core"], pointer.Paths);
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
        Assert.Empty(Assert.Single(surface.Pointers).Paths);
        Assert.Equal("Pointer cleared", cleared.Note);

        Assert.False(Show(surface, null, Enumerable.Repeat("core/vm.gd", BoardService.MaxPaths + 1).ToArray()).Ok);
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
        Assert.Equal(["scenes/holding.gd", "scenes/world.gd"], report.Largest!.Select(n => n.Path));
    }

    [Fact]
    public void Clean_keeps_one_short_line()
    {
        Assert.Equal("a b c", BoardService.Clean("  a\r\n b\t\tc \u0000", 20));
        Assert.Equal("", BoardService.Clean(" \n ", 20));
        Assert.Equal("abcdefghi…", BoardService.Clean("abcdefghijklmnop", 10));
    }
}

public class BoardToolsTests
{
    static T Read<T>(BoardToolResult result) => JsonSerializer.Deserialize<T>(result.Json, ScanJson.Options)!;

    [Fact]
    public async Task Without_a_window_nobody_is_looking_and_summaries_come_from_a_scan()
    {
        using var repo = new TempRepo().Write("scenes/world.gd", "1\n2\n").Write("docs/a.md", "x\n").Commit("Start");
        var tools = new BoardTools(Path.Combine(repo.Root, "scenes"), new GitRunner(), _ => new ScanOptions());

        var selection = JsonDocument.Parse((await tools.SelectionAsync()).Json).RootElement;
        var show = await tools.ShowAsync(["world.gd"], "here", "Claude");
        var summary = Read<SummaryReport>(await tools.SummaryAsync("world.gd"));

        Assert.False(selection.GetProperty("window").GetBoolean());
        Assert.False(show.IsError);
        Assert.False(Read<ShowReport>(show).Window);
        Assert.False(summary.Window);
        Assert.Equal("scenes/world.gd", summary.Path);     // scanned from the repository's top folder
        Assert.Equal(2, summary.Lines);
        Assert.Equal(1, summary.CommitsInRange);
        Assert.True((await tools.SummaryAsync("nope.gd")).IsError);
    }

    [Fact]
    public async Task With_a_window_the_tools_ask_it()
    {
        using var repo = new TempRepo().Write("scenes/world.gd", "1\n").Commit("Start");
        var surface = new FakeSurface(new ProjectIndex(await new ProjectScanner(new GitRunner()).ScanAsync(repo.Root)));
        await using var server = new BoardServer(repo.Root, r => Task.FromResult(BoardService.Handle(surface, r, DateTimeOffset.Now)), _ => { });
        await BoardChannelTests.WaitUntil(() => server.Serving);
        var tools = new BoardTools(repo.Root, new GitRunner(), _ => new ScanOptions());

        var show = Read<ShowReport>(await tools.ShowAsync(["scenes/world.gd", "nope.gd"], "The world", "Claude"));
        var summary = Read<SummaryReport>(await tools.SummaryAsync("scenes"));
        var selection = JsonDocument.Parse((await tools.SelectionAsync()).Json).RootElement;

        Assert.True(show.Window);
        Assert.Equal(["scenes/world.gd"], show.Shown);
        Assert.Equal(["nope.gd"], show.Unknown);
        Assert.Equal("The world", Assert.Single(surface.Pointers).Caption);
        Assert.True(summary.Window);
        Assert.True(selection.GetProperty("window").GetBoolean());
        Assert.True((await tools.SummaryAsync("nope")).IsError);
    }
}

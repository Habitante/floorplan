using System.Diagnostics;
using System.Text.Json;

namespace FloorPlan.Core.Tests;

public class BoardChannelTests
{
    internal static string UniqueRoot() => Path.Combine(Path.GetTempPath(), "floorplan-board", Guid.NewGuid().ToString("N")[..12]);

    internal static async Task WaitUntil(Func<bool> condition, int milliseconds = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > milliseconds) throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    static Func<BoardRequest, Task<BoardReply>> Echo(string tag) =>
        r => Task.FromResult(BoardReply.Success(new { tag, op = r.Op, caption = r.Caption }));

    static string? Field(BoardReply? reply, string name) => reply?.Result?.GetProperty(name).GetString();

    [Fact]
    public async Task Requests_reach_the_window_for_the_folder_or_one_above_it()
    {
        var root = UniqueRoot();
        await using var server = new BoardServer(root, Echo("window"), _ => { });
        await WaitUntil(() => server.Serving);

        var direct = await BoardChannel.RequestAsync(root, new BoardRequest { Op = "selection", Caption = "ñandú ✓" });
        var below = await BoardChannel.RequestAsync(Path.Combine(root, "tools", "blender"), new BoardRequest { Op = "show" });

        Assert.True(direct!.Ok);
        Assert.Equal("selection", Field(direct, "op"));
        Assert.Equal("ñandú ✓", Field(direct, "caption"));
        Assert.Equal("show", Field(below, "op"));
    }

    [Fact]
    public async Task Without_a_window_there_is_no_reply_and_no_wait()
    {
        var clock = Stopwatch.StartNew();
        Assert.Null(await BoardChannel.RequestAsync(UniqueRoot(), new BoardRequest { Op = "selection" }));
        Assert.True(clock.ElapsedMilliseconds < 2000, $"{clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Only_the_first_window_on_a_project_serves_it_until_it_closes()
    {
        var root = UniqueRoot();
        var first = new BoardServer(root, Echo("first"), _ => { }, TimeSpan.FromMilliseconds(50));
        await WaitUntil(() => first.Serving);
        await using var second = new BoardServer(root, Echo("second"), _ => { }, TimeSpan.FromMilliseconds(50));
        await Task.Delay(300);

        Assert.False(second.Serving);
        Assert.Equal("first", Field(await BoardChannel.RequestAsync(root, new BoardRequest { Op = "x" }), "tag"));

        await first.DisposeAsync();
        await WaitUntil(() => second.Serving);
        Assert.Equal("second", Field(await BoardChannel.RequestAsync(root, new BoardRequest { Op = "x" }), "tag"));
    }

    [Fact]
    public async Task Failures_come_back_as_errors()
    {
        var root = UniqueRoot();
        await using var server = new BoardServer(root, _ => throw new InvalidOperationException("broken"), _ => { });
        await WaitUntil(() => server.Serving);

        var failed = await BoardChannel.RequestAsync(root, new BoardRequest { Op = "selection" });
        var version = await BoardChannel.RequestAsync(root, new BoardRequest { V = 99, Op = "selection" });

        Assert.False(failed!.Ok);
        Assert.Equal("broken", failed.Error);
        Assert.False(version!.Ok);
        Assert.Contains("version", version.Error);
    }

    [Fact]
    public void Pipe_names_follow_the_project_folder_not_its_spelling()
    {
        var root = UniqueRoot();
        Assert.Equal(BoardChannel.PipeName(root), BoardChannel.PipeName(root.ToUpperInvariant() + Path.DirectorySeparatorChar));
        Assert.NotEqual(BoardChannel.PipeName(root), BoardChannel.PipeName(Path.Combine(root, "sub")));
        Assert.Equal(BoardChannel.PipeName(root), BoardChannel.Candidates(Path.Combine(root, "a", "b")).ElementAt(2));
    }
}

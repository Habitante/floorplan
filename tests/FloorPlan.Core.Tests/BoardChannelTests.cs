using System.Diagnostics;

namespace FloorPlan.Core.Tests;

/// <summary>
/// Tests that serve real pipes run one at a time: discovery sees every board pipe on the machine,
/// including the test servers of other classes.
/// </summary>
[CollectionDefinition("Board pipes", DisableParallelization = true)]
public class BoardPipes;

[Collection("Board pipes")]
public class BoardChannelTests
{
    /// <summary>A pipe name no real window uses: process ids stay far below this.</summary>
    internal static string UniquePipe() => BoardChannel.PipeName(Random.Shared.Next(1_000_000_000, int.MaxValue));

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
    public async Task Requests_reach_a_window_by_its_pipe()
    {
        var pipe = UniquePipe();
        await using var server = new BoardServer(pipe, Echo("window"), _ => { });
        await WaitUntil(() => server.Serving);

        var reply = await BoardChannel.RequestAsync(pipe, new BoardRequest { Op = "selection", Caption = "ñandú ✓" });

        Assert.True(reply!.Ok);
        Assert.Equal("selection", Field(reply, "op"));
        Assert.Equal("ñandú ✓", Field(reply, "caption"));
    }

    [Fact]
    public async Task A_missing_window_is_no_reply_without_a_wait()
    {
        var clock = Stopwatch.StartNew();
        Assert.Null(await BoardChannel.RequestAsync(UniquePipe(), new BoardRequest { Op = "selection" }, connectMilliseconds: 50));
        Assert.True(clock.ElapsedMilliseconds < 2000, $"{clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Discovery_finds_each_window_and_its_project_used_last_first()
    {
        string a = UniquePipe(), b = UniquePipe(), refusing = UniquePipe();
        var root = Path.Combine(Path.GetTempPath(), "floorplan-board", Guid.NewGuid().ToString("N")[..8]);
        Func<BoardRequest, Task<BoardReply>> Hello(string project, long idle) =>
            _ => Task.FromResult(BoardReply.Success(new HelloReport(Path.Combine(root, project), project, idle, false)));
        await using var first = new BoardServer(a, Hello("old", 300), _ => { });
        await using var second = new BoardServer(b, Hello("new", 5), _ => { });
        await using var third = new BoardServer(refusing, _ => Task.FromResult(BoardReply.Failure("speaks board version 1")), _ => { });
        await WaitUntil(() => first.Serving && second.Serving && third.Serving);

        var mine = (await BoardChannel.FindWindowsAsync()).Where(w => w.Pipe == a || w.Pipe == b || w.Pipe == refusing).ToList();

        Assert.Equal([b, a, refusing], mine.Select(w => w.Pipe));
        Assert.Equal(Path.Combine(root, "new"), mine[0].Project);
        Assert.Equal("new", mine[0].Name);
        Assert.Null(mine[2].Project);
        Assert.Contains("version 1", mine[2].Note);
    }

    [Fact]
    public async Task Failures_come_back_as_errors()
    {
        var pipe = UniquePipe();
        await using var server = new BoardServer(pipe, _ => throw new InvalidOperationException("broken"), _ => { });
        await WaitUntil(() => server.Serving);

        var failed = await BoardChannel.RequestAsync(pipe, new BoardRequest { Op = "selection" });
        var version = await BoardChannel.RequestAsync(pipe, new BoardRequest { V = 1, Op = "selection" });

        Assert.False(failed!.Ok);
        Assert.Equal("broken", failed.Error);
        Assert.False(version!.Ok);
        Assert.Contains("version", version.Error);
    }
}

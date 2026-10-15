using System.Text.Json;

namespace FloorPlan.Core.Tests;

public class BoardTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    static readonly ProjectScanner Scanner = new(new GitRunner());

    [Fact]
    public void Reads_the_view_as_the_front_end_sends_it()
    {
        // The shape of boardView() in web/src/board.ts.
        const string json = """
            {"focus":"scenes","selected":"scenes/world.gd","commit":"working",
             "range":{"from":"aaa","to":"bbb","count":2,"latest":false},
             "reader":{"path":"scenes/world.gd","commit":null,"visible":[1,40],"selection":[3,5]},
             "area":"bytes","fill":"kind"}
            """;
        var view = JsonSerializer.Deserialize<BoardView>(json, ScanJson.Options)!;

        Assert.Equal("scenes", view.Focus);
        Assert.Equal("scenes/world.gd", view.Selected);
        Assert.Equal("working", view.Commit);
        Assert.Equal(new BoardRange("aaa", "bbb", 2, false), view.Range);
        Assert.Equal("scenes/world.gd", view.Reader!.Path);
        Assert.Null(view.Reader.Commit);
        Assert.Equal([1, 40], view.Reader.Visible!);
        Assert.Equal([3, 5], view.Reader.Selection!);
        Assert.Equal("bytes", view.Area);
        Assert.Equal("kind", view.Fill);
        Assert.Null(JsonSerializer.Deserialize<BoardView>("""{"focus":"","reader":null,"range":null}""", ScanJson.Options)!.Reader);
    }

    [Fact]
    public void Stamps_each_selection_with_its_time_and_head()
    {
        var state = new BoardState();
        state.Update(new BoardView { Selected = "scenes" }, "aaa", T0);
        state.Update(new BoardView { Selected = "scenes", Focus = "scenes" }, "bbb", T0.AddSeconds(5));

        Assert.Equal(new Choice("scenes", T0, "aaa"), state.Selected);
        Assert.Empty(state.Recent);

        state.Update(new BoardView { Selected = "scenes/world.gd" }, "bbb", T0.AddSeconds(30));
        state.Update(new BoardView(), "bbb", T0.AddSeconds(40));
        state.Update(new BoardView { Selected = "core" }, "ccc", T0.AddSeconds(50));

        Assert.Equal(new Choice("core", T0.AddSeconds(50), "ccc"), state.Selected);
        Assert.Equal(["scenes/world.gd", "scenes"], state.Recent.Select(r => r.Path));
    }

    [Fact]
    public void Keeps_a_short_list_of_distinct_earlier_selections()
    {
        var state = new BoardState();
        string[] picks = ["a", "b", "c", "a", "d", "e", "f", "g"];
        for (int i = 0; i < picks.Length; i++) state.Update(new BoardView { Selected = picks[i] }, null, T0.AddSeconds(i));

        Assert.Equal(["f", "e", "d", "a", "c"], state.Recent.Select(r => r.Path));
    }

    [Fact]
    public void Reader_is_stamped_when_its_file_or_version_changes_not_when_it_scrolls()
    {
        var state = new BoardState();
        state.Update(new BoardView { Reader = new BoardReader("a.gd", null, [1, 40], null) }, null, T0);
        state.Update(new BoardView { Reader = new BoardReader("a.gd", null, [80, 120], [90, 95]) }, null, T0.AddSeconds(9));
        Assert.Equal(T0, state.Reading?.At);

        state.Update(new BoardView { Reader = new BoardReader("a.gd", "working", [1, 40], null) }, null, T0.AddSeconds(20));
        Assert.Equal(T0.AddSeconds(20), state.Reading?.At);

        state.Update(new BoardView(), null, T0.AddSeconds(30));
        Assert.Null(state.Reading);
    }

    static TempRepo History()
    {
        var repo = new TempRepo()
            .Write("scenes/world.gd", "1\n2\n3\n4\n").Write("scenes/world.tscn", "[node]\n").Write("docs/plan.md", "# Plan\n")
            .Commit("Start");
        repo.Write("scenes/world.gd", "1\n2\n3\n4\n5\n").Write("core/vm.gd", "1\n2\n").Commit("Grow the world");
        repo.Write("docs/plan.md", "# Plan\n\nMore\n").Commit("Plan");
        repo.Write("scenes/world.gd", "1\n2\n3\n4\n5\n6\n").Write("scenes/holding.gd", "1\n").Commit("Hold");
        return repo;
    }

    [Fact]
    public async Task Summarises_a_folder_like_the_map_shows_it()
    {
        using var repo = History();
        repo.Write("scenes/world.gd", "changed\nagain\n");
        var index = new ProjectIndex(await Scanner.ScanAsync(repo.Root));
        var scenes = index.Get("scenes")!;

        var report = BoardReports.Summary(index, scenes, CommitWindow.For(index.Scan, null), "lines", fromWindow: true);

        Assert.True(report.Window);
        Assert.Equal("folder", report.Type);
        Assert.Equal(3, report.Files);
        Assert.Equal(3, report.Lines);                    // world.gd (now 2 lines) + holding.gd; the scene is data
        Assert.Equal(1, report.Composition!["data"].Lines);
        Assert.Equal(2, report.Composition["code"].Files);
        Assert.Equal(3, report.CommitsInRange);           // Start, Grow the world, Hold
        Assert.Equal(["Hold", "Grow the world", "Start"], report.RecentCommits!.Select(c => c.Subject));
        Assert.Equal([2, 1, 2], report.RecentCommits!.Select(c => c.Files!.Value));
        Assert.Equal(["scenes/world.gd"], report.Uncommitted);
        Assert.Equal(["scenes/world.gd", "scenes/holding.gd"], report.Largest!.Select(n => n.Path));
        Assert.Equal(3, report.Largest![0].CommitsInRange);
        Assert.Equal("modified", report.Largest![0].Uncommitted);
    }

    [Fact]
    public async Task Counts_activity_over_the_humans_range()
    {
        using var repo = History();
        var scan = await Scanner.ScanAsync(repo.Root);
        var index = new ProjectIndex(scan);
        var world = index.Get("scenes/world.gd")!;
        string Sha(string subject) => scan.Commits.Single(c => c.Subject == subject).Sha;

        Assert.Equal(3, CommitWindow.For(scan, null).Count(world));
        Assert.Equal(1, CommitWindow.For(scan, new BoardRange(null, null, 2, Latest: true)).Count(world));
        var middle = CommitWindow.For(scan, new BoardRange(Sha("Grow the world"), Sha("Plan"), 2, Latest: false));
        Assert.Equal(["Plan", "Grow the world"], middle.Commits.Select(c => c.Subject));
        Assert.Equal(1, middle.Count(world));
        Assert.Equal(2, middle.Count(index.Tree));

        // A range that no longer resolves falls back to the latest commits.
        var stale = CommitWindow.For(scan, new BoardRange("0000000", "1111111", 2, Latest: false));
        Assert.True(stale.Latest);
        Assert.Equal(4, stale.Commits.Count);
    }

    [Fact]
    public async Task Selection_report_says_what_this_means_and_when_it_was_chosen()
    {
        using var repo = History();
        var scan = await Scanner.ScanAsync(repo.Root);
        var index = new ProjectIndex(scan);
        var state = new BoardState();
        state.Update(new BoardView { Selected = "core" }, "0ld", T0);
        state.Update(new BoardView
        {
            Focus = "scenes",
            Selected = "scenes/world.gd",
            Commit = "working",
            Reader = new BoardReader("scenes/world.gd", scan.Commits[0].Sha, [1, 6], [2, 3]),
            Range = new BoardRange(null, null, 2, Latest: true),
        }, scan.Project.Head, T0.AddSeconds(10));

        var report = BoardReports.Selection(index, state, T0.AddSeconds(70));

        Assert.Equal("scenes", report.Zoom);
        Assert.Equal("scenes/world.gd", report.Selected!.Path);
        Assert.Equal(60, report.Selected.SecondsAgo);
        Assert.Null(report.Selected.SelectedAtHead);      // HEAD has not moved
        Assert.Equal(6, report.Selected.Lines);
        Assert.Equal(1, report.Selected.CommitsInRange);
        Assert.Equal($"diff of commit {scan.Commits[0].Sha[..10]}", report.Reader!.Showing);
        Assert.Equal([2, 3], report.Reader.SelectedLines!);
        Assert.Equal("working", report.Commit!.Sha);
        Assert.Equal(2, report.Range!.Commits);
        Assert.Equal("core", Assert.Single(report.Recent!).Path);
        Assert.Equal("0ld", Assert.Single(report.Recent!).SelectedAtHead);
    }
}

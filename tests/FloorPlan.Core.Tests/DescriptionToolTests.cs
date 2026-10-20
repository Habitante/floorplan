using System.Text.Json;

namespace FloorPlan.Core.Tests;

public class DescriptionToolTests
{
    static T Read<T>(BoardToolResult result) => JsonSerializer.Deserialize<T>(result.Json, ScanJson.Options)!;

    static TempRepo Project() => new TempRepo()
        .Write("scenes/world.gd", "1\n2\n").Write("scenes/player.gd", "1\n").Write("ui/menu.gd", "1\n").Write("README.md", "# P\n")
        .Commit("Start");

    /// <summary>Tools that keep descriptions beside the test repository, never in it.</summary>
    static (BoardTools Tools, string File) Tools(TempRepo repo)
    {
        var file = Path.Combine(Path.GetTempPath(), "floorplan-descriptions", Guid.NewGuid().ToString("N") + ".floorplan.json");
        return (new BoardTools(repo.Root, new GitRunner(), _ => new ScanOptions(), descriptionPath: _ => file), file);
    }

    static string Description(params (string Id, string Path)[] systems) => JsonSerializer.Serialize(new ProjectDescription
    {
        Floorplan = 1,
        Summary = "A test project",
        Systems = systems.Select(s => new SystemEntry { Id = s.Id, Name = s.Id, Code = s.Id[..2].ToUpperInvariant(), Paths = [s.Path] }).ToList(),
        Findings = [new FindingEntry { Title = "World", Paths = ["scenes/world.gd:1-2"] }],
    }, ScanJson.Options);

    [Fact]
    public async Task Without_a_description_says_how_to_write_one()
    {
        using var repo = Project();
        var (tools, file) = Tools(repo);

        var reply = Read<DescriptionReply>(await tools.DescriptionAsync(repo.Root));

        Assert.Equal(file, reply.File);
        Assert.Null(reply.Description);
        Assert.Contains("describe-project", reply.Note);
    }

    [Fact]
    public async Task Describe_saves_a_sound_description_outside_the_project_and_reads_it_back()
    {
        using var repo = Project();
        var (tools, file) = Tools(repo);
        try
        {
            var none = Read<SummaryReport>(await tools.SummaryAsync(null, repo.Root));
            Assert.StartsWith("No description yet", none.DescriptionNote);

            var saved = Read<DescriptionReply>(await tools.DescribeAsync(Description(("world", "scenes")), repo.Root));

            Assert.True(saved.Saved);
            Assert.True(File.Exists(file));
            Assert.False(Directory.EnumerateFiles(repo.Root, "*.floorplan.json", SearchOption.AllDirectories).Any());
            Assert.Equal([new SystemDigest("world", "world", "WO", null, null, 2, 3)], saved.Systems!);
            Assert.Equal([new UnnamedCode("ui", 1, 1)], saved.Unnamed!);

            var read = Read<DescriptionReply>(await tools.DescriptionAsync(repo.Root));
            Assert.Equal("A test project", read.Description!.Summary);
            Assert.Null(read.Stale);

            // The summaries agents already call carry it in brief, and a file's system.
            var whole = Read<SummaryReport>(await tools.SummaryAsync(null, repo.Root));
            Assert.Equal(["world"], whole.Description!.Systems.Select(s => s.Id));
            Assert.Equal(["World"], whole.Description.Findings!);
            Assert.Null(whole.DescriptionNote);
            var player = Read<SummaryReport>(await tools.SummaryAsync("scenes/player.gd", repo.Root));
            Assert.Equal(new SystemRef("world", "world", "WO"), player.System);

            // A file that goes shows as stale on the next read.
            File.Delete(Path.Combine(repo.Root, "scenes", "world.gd"));
            repo.Git("add", "-A");
            repo.Commit("World gone");
            var after = Read<DescriptionReply>(await tools.DescriptionAsync(repo.Root));
            Assert.Equal(["scenes/world.gd:1-2"], after.Stale!.Select(s => s.Anchor));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Describe_refuses_a_description_with_problems_and_keeps_the_last_one()
    {
        using var repo = Project();
        var (tools, file) = Tools(repo);
        try
        {
            Assert.True(Read<DescriptionReply>(await tools.DescribeAsync(Description(("world", "scenes")), repo.Root)).Saved);
            var before = File.ReadAllText(file);

            var refused = Read<DescriptionReply>(await tools.DescribeAsync(Description(("world", "scenes"), ("world", "ui")), repo.Root));
            var garbled = await tools.DescribeAsync("{ not json", repo.Root);

            Assert.False(refused.Saved);
            Assert.Equal(["Two systems with the id world", "Two systems with the code WO"], refused.Problems!);
            Assert.True(garbled.IsError);
            Assert.Equal(before, File.ReadAllText(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void A_window_summary_carries_the_description_it_checked()
    {
        var index = ProjectIndexTests.Index(Path.Combine(Path.GetTempPath(), "floorplan-described"), "scenes/world.gd", "ui/menu.gd");
        var description = new ProjectDescription { Floorplan = 1, Systems = [new SystemEntry { Id = "world", Name = "The world", Paths = ["scenes"] }] };
        var surface = new FakeSurface(index) { Description = Descriptions.Check("d.json", description, index) };

        var reply = BoardService.Handle(surface, new BoardRequest { Op = "summary", Path = "" }, DateTimeOffset.Now);
        var summary = reply.Result!.Value.Deserialize<SummaryReport>(ScanJson.Options)!;

        Assert.Equal("The world", Assert.Single(summary.Description!.Systems).Name);
        Assert.Equal([new UnnamedCode("ui", 1, 1)], summary.Description.Unnamed!);
    }
}

using System.Text.Json;

namespace FloorPlan.Core.Tests;

public class DescriptionTests
{
    static readonly ProjectScanner Scanner = new(new GitRunner());

    static async Task<ProjectIndex> IndexAsync(TempRepo repo) => new(await Scanner.ScanAsync(repo.Root));

    static SystemEntry System(string id, params string[] paths) => new() { Id = id, Name = id, Paths = paths };

    static string? SystemOf(DescriptionReport report, ProjectIndex index, string path) =>
        report.SystemOf![index.Get(path)!.Id!.Value] is int s and >= 0 ? report.Description!.Systems[s].Id : null;

    static TempRepo Project() => new TempRepo()
        .Write("main.gd", "1\n").Write("ui/menu.gd", "1\n").Write("ui/console.gd", "1\n")
        .Write("scenes/world.gd", "1\n").Write("scenes/player.gd", "1\n")
        .Write("tests/test_walk.gd", "1\n").Write("tests/test_rules.gd", "1\n")
        .Write("docs/ROADMAP.md", "a\nb\nc\n").Write("notes.txt", "x\n")
        .Commit("Start");

    [Fact]
    public async Task Gives_each_file_the_system_whose_anchor_names_it_most_specifically()
    {
        using var repo = Project();
        var index = await IndexAsync(repo);
        var description = new ProjectDescription
        {
            Floorplan = 1,
            Systems =
            [
                System("legacy", "main.gd", "ui/**"),
                System("world", "scenes", "tests/test_*.gd"),
                System("shared", "ui/console.gd", "tests/test_rules.gd"),
            ],
        };

        var report = Descriptions.Check("d.json", description, index);

        Assert.Null(report.Problems);
        Assert.Null(report.Stale);
        Assert.Equal("legacy", SystemOf(report, index, "ui/menu.gd"));
        Assert.Equal("shared", SystemOf(report, index, "ui/console.gd"));   // exact beats ui/**
        Assert.Equal("world", SystemOf(report, index, "scenes/player.gd"));              // a folder covers what is beneath it
        Assert.Equal("world", SystemOf(report, index, "tests/test_walk.gd"));
        Assert.Equal("shared", SystemOf(report, index, "tests/test_rules.gd"));
        Assert.Null(SystemOf(report, index, "notes.txt"));
        Assert.Equal([2, 3, 2], report.SystemFiles);
    }

    [Fact]
    public async Task Points_at_the_largest_code_that_no_system_names()
    {
        using var repo = Project();
        var index = await IndexAsync(repo);

        var some = Descriptions.Check("d.json", new ProjectDescription { Floorplan = 1, Systems = [System("world", "scenes")] }, index);
        var all = Descriptions.Check("d.json", new ProjectDescription { Floorplan = 1, Systems = [System("everything", "**")] }, index);

        // Whole folders where nothing is named, and a lone file at a named level; notes.txt is no code.
        Assert.Equal([new UnnamedCode("tests", 2, 2), new UnnamedCode("ui", 2, 2), new UnnamedCode("main.gd", 1, 1)], some.Unnamed!);
        Assert.Null(all.Unnamed);
    }

    [Fact]
    public void Says_in_a_line_when_a_description_needs_writing_or_refreshing()
    {
        var current = new DescriptionReport { File = "d.json", Description = new ProjectDescription { Floorplan = 1 }, Unnamed = [new UnnamedCode("tools/x.py", 1, 190)] };
        var dated = current with
        {
            Stale = [new StaleAnchor("world docs", "docs/a.md", "not in the project")],
            Unnamed = [new UnnamedCode("games/chess", 16, 3041), new UnnamedCode("games/go", 9, 900), new UnnamedCode("tools/x.py", 1, 190)],
        };

        Assert.StartsWith("No description yet: write one with describe", Descriptions.Note(null));
        Assert.StartsWith("The description cannot be read (Unreadable: x)", Descriptions.Note(new DescriptionReport { File = "d.json", Problems = ["Unreadable: x"] }));
        Assert.Null(Descriptions.Note(current));
        Assert.Equal(
            "The description is out of date: 1 anchor is stale; 3,941 lines of code are in no system (largest: games/chess). Refresh it: get_description, then describe.",
            Descriptions.Note(dated));
    }

    [Fact]
    public async Task A_broken_anchor_shows_as_stale_after_a_rescan()
    {
        using var repo = Project();
        var description = new ProjectDescription
        {
            Floorplan = 1,
            Systems = [System("world", "scenes/player.gd", "scenes/world.gd") with { Docs = ["docs/ROADMAP.md:2-3"] }],
            Findings = [new FindingEntry { Title = "Player", Paths = ["scenes/player.gd", "docs/ROADMAP.md:3"] }],
        };
        Assert.Null(Descriptions.Check("d.json", description, await IndexAsync(repo)).Stale);

        File.Delete(Path.Combine(repo.Root, "scenes", "player.gd"));
        repo.Write("docs/ROADMAP.md", "a\n").Commit("Player gone, roadmap shorter");
        var report = Descriptions.Check("d.json", description, await IndexAsync(repo));

        Assert.Equal(
            [
                new StaleAnchor("world paths", "scenes/player.gd", "matches no file"),
                new StaleAnchor("world docs", "docs/ROADMAP.md:2-3", "past the end of the file, which has 1 lines"),
                new StaleAnchor("finding \"Player\"", "scenes/player.gd", "not in the project"),
                new StaleAnchor("finding \"Player\"", "docs/ROADMAP.md:3", "past the end of the file, which has 1 lines"),
            ],
            report.Stale!);
        Assert.Equal([1], report.SystemFiles);      // the anchors that still match keep working
    }

    [Fact]
    public async Task Reads_globs_as_their_own_conventions_say()
    {
        using var repo = new TempRepo()
            .Write("a.gd", "1\n").Write("x/a.gd", "1\n").Write("x/y/a.gd", "1\n").Write("x/y/b.txt", "1\n").Write("Diseño del mundo.md", "1\n")
            .Commit("Start");
        var index = await IndexAsync(repo);
        int Count(string anchor) => Descriptions.Check("d.json", new ProjectDescription { Floorplan = 1, Systems = [System("s", anchor)] }, index).SystemFiles![0];

        Assert.Equal(1, Count("x/*.gd"));         // * stays within a folder
        Assert.Equal(2, Count("x/**/*.gd"));      // **/ also matches no folder
        Assert.Equal(3, Count("**/a.gd"));
        Assert.Equal(3, Count("x/**"));
        Assert.Equal(3, Count("x/"));
        Assert.Equal(1, Count("Diseño del mundo.md"));
        Assert.Equal(1, Count("x\\y\\b.txt"));    // either slash
    }

    [Fact]
    public async Task Reports_what_is_wrong_besides_anchors()
    {
        using var repo = Project();
        var index = await IndexAsync(repo);
        var description = new ProjectDescription
        {
            Floorplan = 2,
            Systems = [System("a", "main.gd") with { Code = "UI" }, System("a", "scenes") with { Code = "ui" }, System("b", "scenes") with { Code = "Wide" }],
            Direction = [new DirectionEntry { Text = "Port", Systems = ["a", "c"] }],
        };

        var report = Descriptions.Check("d.json", description, index);

        Assert.Equal(
            [
                "Format version 2; this FloorPlan reads version 1", "Two systems with the id a", "Two systems with the code ui",
                "The code of b should have one to three characters: \"Wide\"", "Direction names an unknown system: c",
            ],
            report.Problems!);

        var file = Path.Combine(repo.Root, "..", Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, "{ not json");
        try
        {
            Assert.StartsWith("Unreadable:", Assert.Single(Descriptions.Load(file, index).Problems!));
        }
        finally
        {
            File.Delete(file);
        }
        Assert.Equal(["The description file does not exist"], Descriptions.Load(file, index).Problems!);
    }

    [Fact]
    public void The_example_description_reads_as_version_one()
    {
        var file = Path.Combine(RepositoryRoot(), "examples", "floorplan.floorplan.json");
        var description = JsonSerializer.Deserialize<ProjectDescription>(File.ReadAllText(file), ScanJson.Options)!;

        Assert.Equal(1, description.Floorplan);
        Assert.Equal(["core", "window", "board", "front-end"], description.Systems.Select(s => s.Id));
        Assert.All(description.Systems, s => Assert.Contains(s.Basis, new[] { "observed", "declared", "inferred" }));
        Assert.All(description.Findings, f => Assert.NotEmpty(f.Paths));
    }

    static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "FloorPlan.slnx"))) return dir.FullName;
        throw new InvalidOperationException("FloorPlan.slnx not found above the test output");
    }
}

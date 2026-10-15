namespace FloorPlan.Core.Tests;

public class ProjectIndexTests
{
    static readonly string Root = Path.Combine(Path.GetTempPath(), "floorplan-index", "proj");

    /// <summary>An index over an in-memory scan; nothing touches the disk.</summary>
    internal static ProjectIndex Index(string root, params string[] files)
    {
        var records = files.Select((p, i) => new FileRecord(i, p, FileKind.Code, EntryStatus.Ok, 10, 1, null, null));
        var scan = new ProjectScan
        {
            Project = new ProjectInfo { Root = root, Name = Path.GetFileName(root), Scope = "tracked" },
            Tree = TreeBuilder.Build(Path.GetFileName(root), records),
            Commits = [],
            Issues = [],
        };
        return new ProjectIndex(scan);
    }

    [Fact]
    public void Finds_paths_as_agents_write_them()
    {
        var index = Index(Root, "scenes/world.gd", "tools/blender/room.py", "README.md");

        Assert.Equal("scenes/world.gd", index.Find("scenes/world.gd")?.Path);
        Assert.Equal("scenes/world.gd", index.Find(@"scenes\world.gd")?.Path);
        Assert.Equal("scenes/world.gd", index.Find("./scenes/world.gd")?.Path);
        Assert.Equal("scenes/world.gd", index.Find("/scenes/world.gd")?.Path);
        Assert.Equal("scenes/world.gd", index.Find(Path.Combine(Root, "scenes", "world.gd"))?.Path);
        Assert.Equal("scenes", index.Find("scenes/")?.Path);
        Assert.Equal("tools/blender", index.Find("  tools/blender  ")?.Path);
        Assert.Same(index.Tree, index.Find("."));
        Assert.Same(index.Tree, index.Find(""));
    }

    [Fact]
    public void Prefers_the_agents_working_folder_then_the_root()
    {
        var index = Index(Root, "tools/blender/room.py", "blender/room.py", "scenes/world.gd");
        var tools = Path.Combine(Root, "tools");

        Assert.Equal("tools/blender/room.py", index.Find("blender/room.py", tools)?.Path);
        Assert.Equal("scenes/world.gd", index.Find("scenes/world.gd", tools)?.Path);
        Assert.Equal("blender/room.py", index.Find("blender/room.py", Root)?.Path);
    }

    [Fact]
    public void Names_nothing_outside_the_scan()
    {
        var index = Index(Root, "scenes/world.gd");
        var outside = Path.Combine(Path.GetDirectoryName(Root)!, "other", "secret.txt");

        Assert.Null(index.Find("../other/secret.txt"));
        Assert.Null(index.Find(@"scenes\..\..\other\secret.txt"));
        Assert.Null(index.Find(outside));
        Assert.Null(index.Find("scenes/missing.gd"));
        Assert.Null(index.Find(null));
        Assert.False(index.TryGetFile("../other/secret.txt", out _));
        Assert.False(index.TryGetFile("scenes", out _));
        Assert.True(index.TryGetFile("scenes/world.gd", out var full));
        Assert.Equal(Path.Combine(Root, "scenes", "world.gd"), full);
    }

    [Fact]
    public void Ignores_case_only_when_unambiguous()
    {
        var index = Index(Root, "Scenes/World.gd", "docs/Readme.md", "docs/README.md");

        Assert.Equal("Scenes/World.gd", index.Find("scenes/world.gd")?.Path);
        Assert.Null(index.Find("docs/readme.md"));
        Assert.Equal("docs/README.md", index.Find("docs/README.md")?.Path);
        Assert.Null(index.Get("scenes/world.gd"));
    }

    [Fact]
    public void Lists_every_path_below_the_root()
    {
        var index = Index(Root, "a/b/c.gd", "d.md");
        Assert.Equal(["a", "a/b", "a/b/c.gd", "d.md"], index.Paths.Order(StringComparer.Ordinal));
    }
}

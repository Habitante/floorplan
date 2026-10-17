namespace FloorPlan.Core.Tests;

public class ScannerTests
{
    static readonly ProjectScanner Scanner = new(new GitRunner());

    static IEnumerable<TreeNode> Files(TreeNode node) =>
        node.IsFile ? [node] : node.Children!.SelectMany(Files);

    static TreeNode File(ProjectScan scan, string path) => Files(scan.Tree).Single(f => f.Path == path);

    [Fact]
    public async Task Measures_tracked_files_with_spaces_and_unicode()
    {
        using var repo = new TempRepo()
            .Write("dir with space/ñandú.gd", "a\nb\nc\n")
            .Write("docs/guide.md", "# Guide\n\ntext\n")
            .WriteBytes("art/pixel.png", [0x89, 0x50, 0x4E, 0x47, 0, 1, 2])
            .Write("untracked-later.txt", "")
            .Commit("First");
        repo.Write("not-tracked.gd", "x\n");

        var scan = await Scanner.ScanAsync(repo.Root, new ScanOptions { IncludeUntracked = false });

        Assert.Equal("tracked", scan.Project.Scope);
        Assert.True(scan.Project.Git);
        Assert.Equal("main", scan.Project.Branch);
        Assert.DoesNotContain(Files(scan.Tree), f => f.Path == "not-tracked.gd");

        var code = File(scan, "dir with space/ñandú.gd");
        Assert.Equal(FileKind.Code, code.Kind);
        Assert.Equal(3, code.Lines);
        Assert.Equal(6, code.Bytes);

        var image = File(scan, "art/pixel.png");
        Assert.Equal(FileKind.Asset, image.Kind);
        Assert.Null(image.Lines);
        Assert.Equal(7, image.Bytes);

        Assert.Equal(3, scan.Tree.Code);
        Assert.Equal(3, scan.Tree.Docs);
        Assert.Equal(4, scan.Tree.Files);
    }

    [Fact]
    public async Task Maps_commits_to_current_files_and_reports_others()
    {
        using var repo = new TempRepo()
            .Write("a.gd", "1\n").Write("old.md", "x\n").Commit("Add")
            .Write("a.gd", "1\n2\n").Commit("Grow a");
        repo.Git("mv", "old.md", "new.md");
        repo.Commit("Rename");
        System.IO.File.Delete(Path.Combine(repo.Root, "a.gd"));

        var scan = await Scanner.ScanAsync(repo.Root);
        var a = File(scan, "a.gd");
        var renamed = File(scan, "new.md");

        Assert.Equal(EntryStatus.Missing, a.Status);
        Assert.Null(a.Bytes);
        Assert.Equal(["Rename", "Grow a", "Add"], scan.Commits.Select(c => c.Subject));
        Assert.Equal([renamed.Id!.Value], scan.Commits[0].Files);
        Assert.Equal(1, scan.Commits[0].Other);        // old.md no longer exists
        Assert.Equal([a.Id!.Value], scan.Commits[1].Files);
        Assert.Equal(1, scan.Commits[2].Other);        // old.md again
        Assert.True(scan.Project.HistoryComplete);
    }

    [Fact]
    public async Task Respects_commit_limit()
    {
        using var repo = new TempRepo();
        for (int i = 0; i < 5; i++) repo.Write("f.gd", $"{i}\n").Commit($"c{i}");

        var scan = await Scanner.ScanAsync(repo.Root, new ScanOptions { CommitLimit = 3 });

        Assert.Equal(["c4", "c3", "c2"], scan.Commits.Select(c => c.Subject));
        Assert.False(scan.Project.HistoryComplete);
    }

    [Fact]
    public async Task Subfolder_scan_uses_relative_paths_and_its_own_history()
    {
        using var repo = new TempRepo()
            .Write("game/main.gd", "1\n").Write("tools/build.py", "1\n").Commit("Both")
            .Write("tools/build.py", "1\n2\n").Commit("Tools only");

        var scan = await Scanner.ScanAsync(Path.Combine(repo.Root, "game"));

        Assert.Equal("game", scan.Project.Name);
        Assert.Equal(["main.gd"], Files(scan.Tree).Select(f => f.Path));
        Assert.Equal(["Both"], scan.Commits.Select(c => c.Subject));
        Assert.Equal([File(scan, "main.gd").Id!.Value], scan.Commits[0].Files);
    }

    [Fact]
    public async Task Plain_folder_skips_exclusions_and_has_no_history()
    {
        using var folder = new TempRepo(git: false)
            .Write("src/app.ts", "a\nb\n")
            .Write("node_modules/dep/index.js", "x\n");

        var scan = await Scanner.ScanAsync(folder.Root);

        Assert.Equal("folder", scan.Project.Scope);
        Assert.False(scan.Project.Git);
        Assert.Empty(scan.Commits);
        Assert.Equal(["src/app.ts"], Files(scan.Tree).Select(f => f.Path));
    }

    [Fact]
    public async Task Missing_git_executable_falls_back_to_folder_with_issue()
    {
        using var repo = new TempRepo().Write("a.gd", "1\n").Commit("Add");

        var scan = await new ProjectScanner(new GitRunner("floorplan-test-no-such-git"))
            .ScanAsync(repo.Root);

        Assert.Equal("folder", scan.Project.Scope);
        Assert.Contains(scan.Issues, i => i.Message.StartsWith("Git executable not found"));
        Assert.Contains(Files(scan.Tree), f => f.Path == "a.gd");
    }

    [Fact]
    public async Task Scanning_leaves_the_repository_untouched()
    {
        using var repo = new TempRepo().Write("a.gd", "1\n").Commit("Add");
        // Make the index stale so any index refresh would rewrite it.
        System.IO.File.SetLastWriteTimeUtc(Path.Combine(repo.Root, "a.gd"), DateTime.UtcNow.AddMinutes(5));
        var index = Path.Combine(repo.Root, ".git", "index");
        var before = (System.IO.File.GetLastWriteTimeUtc(index), System.IO.File.ReadAllBytes(index));
        var entriesBefore = Directory.GetFileSystemEntries(Path.Combine(repo.Root, ".git"), "*", SearchOption.AllDirectories).Order().ToArray();

        await Scanner.ScanAsync(repo.Root);

        Assert.Equal(before.Item1, System.IO.File.GetLastWriteTimeUtc(index));
        Assert.Equal(before.Item2, System.IO.File.ReadAllBytes(index));
        Assert.Equal(entriesBefore, Directory.GetFileSystemEntries(Path.Combine(repo.Root, ".git"), "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Fact]
    public async Task Bad_path_is_reported()
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            Scanner.ScanAsync(Path.Combine(Path.GetTempPath(), "floorplan-tests", "does-not-exist")));
    }
}

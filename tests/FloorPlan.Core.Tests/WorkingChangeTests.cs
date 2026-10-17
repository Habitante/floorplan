using System.Text;

namespace FloorPlan.Core.Tests;

public class WorkingChangeTests
{
    static readonly ProjectScanner Scanner = new(new GitRunner());

    static IEnumerable<TreeNode> Files(TreeNode node) =>
        node.IsFile ? [node] : node.Children!.SelectMany(Files);

    [Fact]
    public void Parses_status_including_renames()
    {
        var output = Encoding.UTF8.GetBytes(
            " M dir with space/ñandú.gd\0R  renamed2.md\0renamed.md\0?? fresh.gd\0D  gone.gd\0A  added.gd\0?? nested/\0");

        var renamedFrom = new Dictionary<string, string>();
        var changes = GitParsers.ParseStatus(output, renamedFrom);

        Assert.Equal(WorkingChange.Modified, changes["dir with space/ñandú.gd"]);
        Assert.Equal(WorkingChange.Renamed, changes["renamed2.md"]);
        Assert.False(changes.ContainsKey("renamed.md"));
        Assert.Equal("renamed.md", Assert.Single(renamedFrom, r => r.Key == "renamed2.md").Value);
        Assert.Single(renamedFrom);
        Assert.Equal(WorkingChange.Untracked, changes["fresh.gd"]);
        Assert.Equal(WorkingChange.Deleted, changes["gone.gd"]);
        Assert.Equal(WorkingChange.Added, changes["added.gd"]);
        Assert.Equal(WorkingChange.Untracked, changes["nested/"]);
    }

    [Fact]
    public async Task Scan_marks_uncommitted_changes_and_can_hide_untracked_files()
    {
        using var repo = new TempRepo().Write("a.gd", "1\n").Write("b.gd", "1\n").Write("c.gd", "1\n").Commit("Add");
        repo.Write("a.gd", "1\n2\n").Write("new.gd", "x\n").Write("staged.gd", "y\n");
        repo.Git("add", "staged.gd");
        File.Delete(Path.Combine(repo.Root, "c.gd"));

        var scan = await Scanner.ScanAsync(repo.Root, new ScanOptions { IncludeUntracked = false });
        var files = Files(scan.Tree).ToDictionary(f => f.Path);

        Assert.Equal(WorkingChange.Modified, files["a.gd"].Change);
        Assert.Null(files["b.gd"].Change);
        Assert.Equal(WorkingChange.Deleted, files["c.gd"].Change);
        Assert.Equal(WorkingChange.Added, files["staged.gd"].Change);
        Assert.False(files.ContainsKey("new.gd"));
        Assert.Equal(4, scan.Project.Changes);
        Assert.Equal(1, scan.Project.UntrackedHidden);
        Assert.Equal("tracked", scan.Project.Scope);
    }

    [Fact]
    public async Task A_staged_rename_is_new_at_its_path_and_remembers_the_old_one()
    {
        using var repo = new TempRepo().Write("scenes/old name.gd", "1\n2\n").Commit("Add");
        repo.Git("mv", "scenes/old name.gd", "scenes/new name.gd");

        var files = Files((await Scanner.ScanAsync(repo.Root)).Tree).ToDictionary(f => f.Path);

        Assert.Equal(WorkingChange.Renamed, files["scenes/new name.gd"].Change);
        Assert.Equal("scenes/old name.gd", files["scenes/new name.gd"].RenamedFrom);
        Assert.False(files.ContainsKey("scenes/old name.gd"));
    }

    [Fact]
    public async Task New_files_are_shown_by_default_but_not_ignored_ones()
    {
        using var repo = new TempRepo().Write(".gitignore", "cache/\n").Write("a.gd", "1\n").Commit("Add");
        repo.Write("new.gd", "x\ny\n").Write("cache/junk.bin", "z");

        var scan = await Scanner.ScanAsync(repo.Root);
        var files = Files(scan.Tree).ToDictionary(f => f.Path);

        Assert.Equal("tracked+untracked", scan.Project.Scope);
        Assert.Equal(WorkingChange.Untracked, files["new.gd"].Change);
        Assert.Equal(2, files["new.gd"].Lines);
        Assert.False(files.ContainsKey("cache/junk.bin"));
        Assert.Equal(0, scan.Project.UntrackedHidden);
    }

    [Fact]
    public async Task Subfolder_scan_reports_changes_relative_to_itself()
    {
        using var repo = new TempRepo().Write("game/main.gd", "1\n").Write("tools/t.py", "1\n").Commit("Add");
        repo.Write("game/main.gd", "1\n2\n").Write("tools/t.py", "2\n");

        var scan = await Scanner.ScanAsync(Path.Combine(repo.Root, "game"));

        Assert.Equal(WorkingChange.Modified, Files(scan.Tree).Single(f => f.Path == "main.gd").Change);
        Assert.Equal(1, scan.Project.Changes);
    }

    [Fact]
    public async Task Relevance_ignores_index_and_ignored_paths_but_not_commits_or_project_files()
    {
        using var repo = new TempRepo().Write(".gitignore", ".godot/\n").Write("a.gd", "1\n").Commit("Add");
        var git = new GitRunner();
        var known = new HashSet<string>(StringComparer.Ordinal) { "a.gd" };
        var none = new HashSet<string>();
        Task<bool> Relevant(params string[] paths) =>
            ChangeRelevance.AnyRelevantAsync(git, repo.Root, true, paths, known, none, CancellationToken.None);

        Assert.False(await Relevant(".git/index", ".git/index.lock", ".godot/editor/cache.cfg"));
        Assert.True(await Relevant(".git/HEAD"));
        Assert.True(await Relevant(".git/refs/heads/main"));
        Assert.True(await Relevant("a.gd"));
        Assert.True(await Relevant(".godot/x.cfg", "brand_new.gd"));
    }

    [Fact]
    public async Task Watcher_reports_a_batch_after_the_folder_goes_quiet()
    {
        using var folder = new TempRepo(git: false);
        var received = new TaskCompletionSource<IReadOnlyCollection<string>>();
        using var watcher = new ProjectWatcher(folder.Root, null, (paths, _) =>
        {
            received.TrySetResult(paths);
            return Task.CompletedTask;
        }, quiet: TimeSpan.FromMilliseconds(100));

        folder.Write("src/one.gd", "1\n");
        folder.Write("src/two.gd", "2\n");

        var batch = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("src/one.gd", batch);
        Assert.Contains("src/two.gd", batch);
    }
}

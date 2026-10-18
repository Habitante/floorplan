namespace FloorPlan.Core.Tests;

public class ChangesWithTests
{
    static readonly ProjectScanner Scanner = new(new GitRunner());

    static async Task<ProjectIndex> IndexAsync(TempRepo repo) => new(await Scanner.ScanAsync(repo.Root));

    static FileRelations Relations(ProjectIndex index, string path) => ChangesWith.For(index, index.Get(path)!)!;

    /// <summary>Commits that touched every one of the paths, as Git lists them.</summary>
    static int Together(TempRepo repo, params string[] paths) =>
        paths.Select(p => repo.Git("log", "--no-merges", "--format=%H", "--", p).Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet())
            .Aggregate((a, b) => { a.IntersectWith(b); return a; }).Count;

    [Fact]
    public async Task Counts_shared_commits_as_git_log_does()
    {
        using var repo = new TempRepo().Write("a.gd", "0\n").Write("b.gd", "0\n").Write("c.gd", "0\n").Commit("Start");
        for (int i = 1; i <= 3; i++) repo.Write("a.gd", $"{i}\n").Write("b.gd", $"{i}\n").Commit($"Both {i}");
        repo.Write("a.gd", "4\n").Commit("Only a");
        repo.Write("b.gd", "5\n").Write("c.gd", "5\n").Commit("b and c");

        var index = await IndexAsync(repo);
        var a = Relations(index, "a.gd");

        Assert.Equal(5, a.Commits);
        var b = Assert.Single(a.ChangesWith, p => p.Path == "b.gd");
        Assert.Equal(Together(repo, "a.gd", "b.gd"), b.Together);
        Assert.Equal(4, b.Together);                         // the three, and the first commit
        Assert.Equal(5, b.Commits);
        Assert.Equal(Head(repo, "HEAD~2")[..10], b.Shared[0]);  // newest first
        // c shares only the first commit with a: one is not enough.
        Assert.DoesNotContain(a.ChangesWith, p => p.Path == "c.gd");
    }

    [Fact]
    public async Task A_broad_commit_does_not_relate_everything_to_everything()
    {
        using var repo = new TempRepo().Write("a.gd", "0\n").Write("b.gd", "0\n").Commit("Start");
        // Two sweeps over thirty files, as a formatter or a rename would make.
        for (int sweep = 1; sweep <= 2; sweep++)
        {
            repo.Write("a.gd", $"sweep {sweep}\n");
            for (int f = 0; f < 30; f++) repo.Write($"swept/f{f:00}.gd", $"{sweep}\n");
            repo.Commit($"Sweep {sweep}");
        }
        repo.Write("a.gd", "x\n").Write("b.gd", "x\n").Commit("Small 1");
        repo.Write("a.gd", "y\n").Write("b.gd", "y\n").Commit("Small 2");

        var a = Relations(await IndexAsync(repo), "a.gd");

        Assert.Equal(2, Together(repo, "a.gd", "swept/f00.gd"));
        Assert.Equal(["b.gd"], a.ChangesWith.Select(p => p.Path));
        Assert.True(ChangesWith.Weight(new CommitInfo { Sha = "x", Author = "t", Subject = "s", Files = new int[31] }) < 0.5);
    }

    [Fact]
    public async Task A_file_in_nearly_every_commit_ranks_below_a_close_partner()
    {
        using var repo = new TempRepo().Write("NOTES.md", "0\n").Commit("Start");
        for (int i = 1; i <= 8; i++) repo.Write("NOTES.md", $"{i}\n").Write($"other{i % 4}.gd", $"{i}\n").Commit($"Work {i}");
        for (int i = 1; i <= 3; i++) repo.Write("player.gd", $"{i}\n").Write("glow.gdshader", $"{i}\n").Write("NOTES.md", $"player {i}\n").Commit($"Player {i}");

        var player = Relations(await IndexAsync(repo), "player.gd");

        Assert.Equal(["glow.gdshader", "NOTES.md"], player.ChangesWith.Select(p => p.Path));
        Assert.Equal(1, player.ChangesWith[0].Strength);
        Assert.True(player.ChangesWith[1].Strength < 0.6);
        Assert.Equal(3, player.ChangesWith[1].Together);
        Assert.Equal(12, player.ChangesWith[1].Commits);
    }

    [Fact]
    public async Task Pairs_tests_with_code_by_name_where_the_conventions_allow()
    {
        using var repo = new TempRepo()
            .Write("scenes/player.gd", "1\n").Write("tests/test_player.gd", "1\n").Write("tests/test_players.gd", "1\n")
            .Write("packages/lib/material/text_field.dart", "1\n").Write("packages/lib/cupertino/text_field.dart", "1\n")
            .Write("packages/test/material/text_field_test.dart", "1\n").Write("packages/test/cupertino/text_field_test.dart", "1\n")
            .Write("src/Player.cs", "1\n").Write("tests/PlayerTests.cs", "1\n")
            .Write("docs/player.md", "1\n")
            .Commit("Start");
        var index = await IndexAsync(repo);
        IReadOnlyList<string> Paired(string path) => Relations(index, path).PairedByName;

        Assert.Equal(["tests/test_player.gd"], Paired("scenes/player.gd"));    // not test_players.gd: plurals are no convention
        Assert.Equal(["scenes/player.gd"], Paired("tests/test_player.gd"));
        Assert.Empty(Paired("tests/test_players.gd"));
        // Two files share the name; each test pairs with the one in its own folders.
        Assert.Equal(["packages/test/material/text_field_test.dart"], Paired("packages/lib/material/text_field.dart"));
        Assert.Equal(["packages/lib/cupertino/text_field.dart"], Paired("packages/test/cupertino/text_field_test.dart"));
        Assert.Equal(["tests/PlayerTests.cs"], Paired("src/Player.cs"));
        Assert.Empty(Paired("docs/player.md"));
    }

    [Theory]
    [InlineData("test_player.gd", "player")]
    [InlineData("player_test.go", "player")]
    [InlineData("text_field_test.dart", "text_field")]
    [InlineData("menu.test.ts", "menu")]
    [InlineData("menu.spec.js", "menu")]
    [InlineData("PlayerTests.cs", "Player")]
    [InlineData("PlayerTest.java", "Player")]
    [InlineData("latest.py", null)]
    [InlineData("test.py", null)]
    [InlineData("conftest.py", null)]
    public void Names_the_code_a_test_is_named_for(string name, string? tested) => Assert.Equal(tested, FileClassifier.TestedName(name));

    static string Head(TempRepo repo, string revision) => repo.Git("rev-parse", revision).Trim();
}

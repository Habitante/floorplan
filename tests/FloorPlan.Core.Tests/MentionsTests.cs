namespace FloorPlan.Core.Tests;

public class MentionsTests
{
    [Fact]
    public async Task Finds_the_files_that_mention_a_name_as_a_whole_word_most_first()
    {
        using var repo = new TempRepo()
            .Write("scenes/player.gd", "func jump():\n    pass\n")
            .Write("scenes/hud.gd", "player.jump()\n# jumpy is not jump\nplayer.jump()\n")
            .Write("docs/notes.md", "Call jump to leave the ground.\n")
            .Write("scenes/far.gd", "jumper()\n")
            .Write(".gitignore", "build/\n")
            .Commit("Start");
        repo.Write("scenes/new.gd", "jump()\n").Write("build/out.gd", "jump()\n");

        var report = await Mentions.FindAsync(new GitRunner(), repo.Root, "jump", "scenes/player.gd", CancellationToken.None);

        // The declaring file is left out, a new file is in, an ignored one and a longer word are not.
        Assert.Equal(["scenes/hud.gd", "docs/notes.md", "scenes/new.gd"], report!.Files.Select(f => f.Path));
        Assert.Equal(3, report.Files[0].Count);
        Assert.Equal([1, 2, 3], report.Files[0].Lines);
        Assert.Null(report.FilesNotListed);
        Assert.Empty((await Mentions.FindAsync(new GitRunner(), repo.Root, "nothing", null, CancellationToken.None))!.Files);
    }

    [Fact]
    public async Task Searches_only_names_and_only_repositories()
    {
        Assert.True(Mentions.Searchable("_ready"));
        Assert.True(Mentions.Searchable("Größe"));
        Assert.False(Mentions.Searchable("x"));
        Assert.False(Mentions.Searchable("a.b"));
        Assert.False(Mentions.Searchable("--all"));
        using var plain = new TempRepo(git: false).Write("a.gd", "jump()\n");
        Assert.Null(await Mentions.FindAsync(new GitRunner(), plain.Root, "jump", null, CancellationToken.None));
    }
}

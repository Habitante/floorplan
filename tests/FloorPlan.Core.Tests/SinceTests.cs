namespace FloorPlan.Core.Tests;

public class SinceTests
{
    static readonly ProjectScanner Scanner = new(new GitRunner());
    static readonly DateTimeOffset Then = new(2026, 10, 16, 18, 0, 0, TimeSpan.Zero);
    const string Claude = "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>";

    static string Head(TempRepo repo, string revision = "HEAD") => repo.Git("rev-parse", revision).Trim();

    static async Task<SinceReport?> SinceAsync(TempRepo repo, string seen, ScanOptions? options = null, string? folder = null)
    {
        var index = new ProjectIndex(await Scanner.ScanAsync(folder ?? repo.Root, options));
        return await Since.ComputeAsync(new GitRunner(), index, new SeenMark(seen, Then), CancellationToken.None);
    }

    [Fact]
    public async Task Counts_what_landed_since_the_seen_head_as_git_log_does()
    {
        using var repo = new TempRepo().Write("tools/build.py", "1\n").Write("README.md", "# P\n").Commit("Start");
        var seen = Head(repo);
        repo.Write("tools/build.py", "1\n2\n").Commit($"Faster builds\n\n{Claude}");
        repo.Write("tools/rack.py", "1\n").Write("scenes/world.gd", "1\n").Commit($"Racks\n\n{Claude}\nCo-Authored-By: Codex <codex@openai.com>");
        repo.Write("README.md", "# P\n\nMore\n").Commit("Notes");

        var since = (await SinceAsync(repo, seen))!;

        Assert.Equal(int.Parse(repo.Git("rev-list", "--count", "--no-merges", $"{seen}..HEAD").Trim()), since.Commits);
        Assert.Equal(3, since.Commits);
        Assert.Equal((seen[..10], Then), (since.SeenHead, since.SeenAt));
        Assert.Equal(Head(repo, "HEAD~2")[..10], since.From);      // the oldest of them: the range starts there
        // README.md is at the top level, which counts as one region named "".
        Assert.Equal([new Tally("tools", 2), new Tally("", 1), new Tally("scenes", 1)], since.Regions!);
        Assert.Equal([new Tally("Test", 3)], since.Authors!);
        Assert.Equal([new Tally("Claude Opus 5.5", 2), new Tally("Codex", 1)], since.CoAuthors!);
        Assert.Null(since.BeyondHistory);
        Assert.Null(since.Problem);
    }

    [Fact]
    public async Task Nothing_new_is_no_briefing()
    {
        using var repo = new TempRepo().Write("a.gd", "1\n").Commit("Start");
        var index = new ProjectIndex(await Scanner.ScanAsync(repo.Root));

        Assert.Null(await SinceAsync(repo, Head(repo)));
        Assert.Null(await SinceAsync(repo, "--output=x"));
        Assert.Null(await Since.ComputeAsync(new GitRunner(), index, null, CancellationToken.None));
    }

    [Fact]
    public async Task Says_so_when_the_seen_commit_left_the_history()
    {
        using var repo = new TempRepo().Write("a.gd", "1\n").Commit("Start");
        repo.Write("a.gd", "2\n").Commit("Seen, then reset away");
        var seen = Head(repo);
        repo.Git("reset", "-q", "--hard", "HEAD~1");
        repo.Write("a.gd", "3\n").Commit("Rewritten");

        var since = (await SinceAsync(repo, seen))!;

        Assert.Equal(Since.LostProblem, since.Problem);
        Assert.Equal(0, since.Commits);
        Assert.Null(since.From);
        Assert.Equal(Since.LostProblem, (await SinceAsync(repo, new string('0', 40)))!.Problem);
    }

    [Fact]
    public async Task Counts_commits_beyond_the_loaded_history_but_describes_the_loaded_ones()
    {
        using var repo = new TempRepo().Write("a.gd", "0\n").Commit("Start");
        var seen = Head(repo);
        for (int i = 1; i <= 4; i++) repo.Write("a.gd", $"{i}\n").Commit($"Step {i}");

        var since = (await SinceAsync(repo, seen, new ScanOptions { CommitLimit = 2 }))!;

        Assert.Equal(4, since.Commits);
        Assert.True(since.BeyondHistory);
        Assert.Equal(Head(repo, "HEAD~1")[..10], since.From);      // the oldest loaded
        Assert.Equal([new Tally("", 2)], since.Regions!);
    }

    [Fact]
    public async Task Lists_the_names_with_most_commits_and_counts_the_rest()
    {
        using var repo = new TempRepo().Write("a.gd", "0\n").Commit("Start");
        var seen = Head(repo);
        // Twelve authors; the last two commit twice, so they lead the list.
        for (int i = 1; i <= 14; i++)
        {
            int person = i <= 12 ? i : i - 2;
            repo.Write("a.gd", $"{i}\n").Git("add", "-A");
            repo.Git("-c", $"user.name=Person {person:00}", "-c", "user.email=p@example.com", "commit", "-q", "-m", $"Step {i}");
        }

        var since = (await SinceAsync(repo, seen))!;

        Assert.Equal(14, since.Commits);
        Assert.Equal(Since.TallyLimit, since.Authors!.Count);
        Assert.Equal([new Tally("Person 11", 2), new Tally("Person 12", 2), new Tally("Person 01", 1)], since.Authors.Take(3));
        Assert.Equal(12 - Since.TallyLimit, since.MoreAuthors);
        Assert.Null(since.MoreRegions);
        Assert.Null(since.CoAuthors);
        Assert.Null(since.MoreCoAuthors);
    }

    [Fact]
    public async Task A_merge_seen_as_head_counts_from_there()
    {
        using var repo = new TempRepo().Write("a.gd", "1\n").Commit("Start");
        repo.Git("checkout", "-q", "-b", "side");
        repo.Write("b.gd", "1\n").Commit("Side");
        repo.Git("checkout", "-q", "main");
        repo.Write("c.gd", "1\n").Commit("Main");
        repo.Git("-c", "user.name=Test", "-c", "user.email=test@example.com", "merge", "-q", "--no-ff", "-m", "Merge side", "side");
        var seen = Head(repo);      // the scan's history leaves merges out
        repo.Write("d.gd", "1\n").Commit("After the merge");

        var since = (await SinceAsync(repo, seen))!;

        Assert.Equal(1, since.Commits);
        Assert.Equal(Head(repo)[..10], since.From);
        Assert.Equal([new Tally("", 1)], since.Regions!);
    }

    [Fact]
    public async Task From_a_subfolder_only_its_commits_count()
    {
        using var repo = new TempRepo().Write("tools/a.py", "1\n").Write("docs/a.md", "x\n").Commit("Start");
        var seen = Head(repo);
        repo.Write("tools/a.py", "2\n").Commit("Tools");
        repo.Write("docs/a.md", "y\n").Commit("Docs");

        var since = (await SinceAsync(repo, seen, folder: Path.Combine(repo.Root, "tools")))!;

        Assert.Equal(1, since.Commits);
        Assert.Equal(Head(repo, "HEAD~1")[..10], since.From);
        Assert.Equal([new Tally("", 1)], since.Regions!);       // a.py, at the top of the scanned folder
    }
}

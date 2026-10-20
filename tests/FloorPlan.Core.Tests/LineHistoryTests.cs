namespace FloorPlan.Core.Tests;

public class LineHistoryTests
{
    static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

    [Fact]
    public async Task Follows_each_commits_lines_into_the_working_file()
    {
        using var repo = new TempRepo()
            .Write("a.gd", Lines("1", "2", "3", "4", "5", "6", "7", "8", "9", "10")).Commit("Start")
            .Write("a.gd", Lines("1", "2", "3", "4", "5", "six", "7", "8", "9", "10")).Commit("Six")
            .Write("a.gd", Lines("a", "b", "1", "2", "3", "4", "5", "six", "7", "8", "9", "10")).Commit("Two at the top");
        // Uncommitted: "2" deleted, "9" changed.
        repo.Write("a.gd", Lines("a", "b", "1", "3", "4", "5", "six", "7", "8", "nine", "10"));

        var history = await LineHistory.ForAsync(new GitRunner(), repo.Root, "a.gd", CancellationToken.None);

        Assert.Equal(3, history.Commits.Count);
        Assert.Equal([new LineChange(1, 2, 0)], history.Commits[0].Lines);       // the two lines at the top
        Assert.Equal([new LineChange(7, 7, 1)], history.Commits[1].Lines);       // "six", moved down two and up one
        Assert.Equal([new LineChange(3, 11, 0)], history.Commits[2].Lines);      // the first version, less the deleted line
        Assert.Equal([new LineChange(4, 3, 1), new LineChange(10, 10, 1)], history.Working);
        Assert.Equal(repo.Git("rev-parse", "HEAD").Trim(), history.Commits[0].Sha);
    }

    [Fact]
    public void A_line_rewritten_later_counts_for_both_commits_where_the_rewrite_left_it()
    {
        var older = LineHistory.ParseHunks("@@ -6 +6 @@\n-6\n+six\n");
        // Lines 5-7 replaced by two lines, then a line inserted after line 1.
        var rewrite = LineHistory.ParseHunks("@@ -5,3 +5,2 @@\n-5\n-six\n-7\n+five\n+seven\n");
        var insert = LineHistory.ParseHunks("@@ -1,0 +2 @@\n+new\n");

        var history = LineHistory.Follow([("insert", insert), ("rewrite", rewrite), ("older", older)], []);

        Assert.Equal([new LineChange(2, 2, 0)], history.Commits[0].Lines);
        Assert.Equal([new LineChange(6, 7, 3)], history.Commits[1].Lines);
        Assert.Equal([new LineChange(6, 7, 1)], history.Commits[2].Lines);      // grown to the lines that replaced it
    }

    [Fact]
    public void Removed_lines_leave_an_empty_range_where_they_were()
    {
        var removal = LineHistory.ParseHunks("@@ -3,2 +2,0 @@\n-3\n-4\n");
        var later = LineHistory.ParseHunks("@@ -0,0 +1 @@\n+top\n");

        var history = LineHistory.Follow([("later", later), ("removal", removal)], []);

        Assert.Equal([new LineChange(4, 3, 2)], history.Commits[1].Lines);     // before line 4, after the line added on top
    }

    [Fact]
    public async Task A_file_never_committed_has_no_history()
    {
        using var repo = new TempRepo().Write("a.gd", "1\n").Commit("Start");
        repo.Write("new.gd", "1\n2\n");

        var history = await LineHistory.ForAsync(new GitRunner(), repo.Root, "new.gd", CancellationToken.None);

        Assert.Empty(history.Commits);
        Assert.Empty(history.Working);
    }
}

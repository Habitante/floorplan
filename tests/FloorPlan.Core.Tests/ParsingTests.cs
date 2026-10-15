using System.Text;

namespace FloorPlan.Core.Tests;

public class ParsingTests
{
    static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 1)]
    [InlineData("a\n", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\r\nb\r\n", 2)]
    [InlineData("\n\n\n", 3)]
    public void Counts_physical_lines(string text, int expected) =>
        Assert.Equal(expected, LineCounter.Count(Bytes(text)));

    [Fact]
    public void Binary_content_has_no_line_count() =>
        Assert.Null(LineCounter.Count([0x89, 0x50, 0x4E, 0x47, 0x00, 0x0A]));

    [Fact]
    public void Parses_log_with_empty_commit_and_unicode_paths()
    {
        // Shape observed from git 2.51: RS header NUL, then LF and NUL-terminated paths.
        var output = Bytes(
            "\u001eaaa\u001f1792057536\u001fTëst User\u001fEmpty\0" +
            "\u001ebbb\u001f1792057500\u001fTëst User\u001fSecond: añadir\0\ndir with space/ñandú.gd\0renamed.md\0top.md\0");

        var commits = GitParsers.ParseLog(output);

        Assert.Equal(2, commits.Count);
        Assert.Equal("aaa", commits[0].Sha);
        Assert.Empty(commits[0].Paths);
        Assert.Equal(1792057500, commits[1].Time);
        Assert.Equal("Tëst User", commits[1].Author);
        Assert.Equal("Second: añadir", commits[1].Subject);
        Assert.Equal(["dir with space/ñandú.gd", "renamed.md", "top.md"], commits[1].Paths);
    }

    [Fact]
    public void Parses_ls_files_and_collapses_conflict_stages()
    {
        var output = Bytes(
            "100644 de98 0\tdir with space/ñandú.gd\0" +
            "160000 c1b0 0\tvendor/lib\0" +
            "100644 1111 1\tconflict.txt\0100644 2222 2\tconflict.txt\0100644 3333 3\tconflict.txt\0");

        var paths = GitParsers.ParseLsFiles(output);

        Assert.Equal(["dir with space/ñandú.gd", "vendor/lib", "conflict.txt"], paths.Select(p => p.Path));
        Assert.Equal("160000", paths[1].Mode);
    }

    [Fact]
    public void Aggregates_each_file_once_into_every_ancestor()
    {
        var time = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        var tree = TreeBuilder.Build("p",
        [
            new FileRecord(0, "a/b/x.gd", FileKind.Code, EntryStatus.Ok, 100, 10, time, null),
            new FileRecord(1, "a/b/y.md", FileKind.Docs, EntryStatus.Ok, 50, 5, time.AddDays(2), null),
            new FileRecord(2, "a/z.png", FileKind.Asset, EntryStatus.Ok, 1000, null, time, null),
            new FileRecord(3, "a/big.gd", FileKind.Code, EntryStatus.Ok, 9, null, time, "Too large"),
            new FileRecord(4, "gone.gd", FileKind.Code, EntryStatus.Missing, null, null, null, null),
        ]);

        Assert.Equal(5, tree.Files);
        Assert.Equal(1159, tree.Bytes);
        Assert.Equal(10, tree.Code);
        Assert.Equal(5, tree.Docs);
        Assert.Equal(2, tree.Unmeasured); // big.gd has no line count, gone.gd has no size
        Assert.Equal(time.AddDays(2), tree.Modified);

        var a = tree.Children!.Single(c => c.Name == "a");
        var b = a.Children!.Single(c => c.Name == "b");
        Assert.Equal(4, a.Files);
        Assert.Equal(150, b.Bytes);
        Assert.Equal("a/b", b.Path);
        Assert.Equal(["b", "big.gd", "z.png"], a.Children!.Select(c => c.Name));
    }
}

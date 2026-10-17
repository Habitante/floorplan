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
            "\u001eaaa\u001f1792057536\u001fTëst User\u001f\u001fEmpty\0" +
            "\u001ebbb\u001f1792057500\u001fTëst User\u001f\u001fSecond: añadir\0\ndir with space/ñandú.gd\0renamed.md\0top.md\0");

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
    public void Reads_co_authors_once_each_and_not_the_author()
    {
        const char RS = '\x1e', US = '\x1f', GS = '\x1d';
        var output = Bytes(
            $"{RS}aaa{US}1792057536{US}Habitante{US}Claude Opus 5.5 <noreply@anthropic.com>{GS}Codex <codex@openai.com>{GS}" +
            $"claude opus 5.5 <other@example.com>{GS}Habitante <h@example.com>{US}Racks\0" +
            $"{RS}bbb{US}1792057500{US}Habitante{US}%(trailers:key=Co-authored-by){US}An older Git\0" +
            $"{RS}ccc{US}1792057400{US}Habitante{US}<agent@example.com>{US}An address only\0");

        var commits = GitParsers.ParseLog(output);

        Assert.Equal(["Claude Opus 5.5", "Codex"], commits[0].CoAuthors);
        Assert.Equal("Racks", commits[0].Subject);
        Assert.Empty(commits[1].CoAuthors);
        Assert.Equal("An older Git", commits[1].Subject);
        Assert.Equal(["agent@example.com"], commits[2].CoAuthors);
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

    [Theory]
    [InlineData("tests/test_places.gd", FileKind.Test)]
    [InlineData("src/Tests/Board.cs", FileKind.Test)]
    [InlineData("pkg/__tests__/view.tsx", FileKind.Test)]
    [InlineData("web/src/board.test.ts", FileKind.Test)]
    [InlineData("web/src/board.spec.js", FileKind.Test)]
    [InlineData("parser/lexer_test.go", FileKind.Test)]
    [InlineData("tools/test_rack.py", FileKind.Test)]
    [InlineData("conftest.py", FileKind.Test)]
    [InlineData("Core/ScannerTests.cs", FileKind.Test)]
    [InlineData("Core/ScannerTest.cs", FileKind.Test)]
    [InlineData("core/latest.gd", FileKind.Code)]
    [InlineData("core/contest.py", FileKind.Code)]
    [InlineData("core/Manifest.cs", FileKind.Code)]
    [InlineData("core/testing_utils.py", FileKind.Code)]
    [InlineData("tests/errors.txt", FileKind.Docs)]
    [InlineData("tests/fixture.json", FileKind.Data)]
    [InlineData("tests/screen.png", FileKind.Asset)]
    public void Marks_code_under_test_conventions_as_tests(string path, FileKind expected) =>
        Assert.Equal(expected, FileClassifier.FromName(path));

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
            new FileRecord(5, "a/tests/x_test.gd", FileKind.Test, EntryStatus.Ok, 30, 3, time, null),
        ]);

        Assert.Equal(6, tree.Files);
        Assert.Equal(1189, tree.Bytes);
        Assert.Equal(10, tree.Code);
        Assert.Equal(3, tree.Tests);
        Assert.Equal(5, tree.Docs);
        Assert.Equal(2, tree.Unmeasured); // big.gd has no line count, gone.gd has no size
        Assert.Equal(time.AddDays(2), tree.Modified);

        var a = tree.Children!.Single(c => c.Name == "a");
        var b = a.Children!.Single(c => c.Name == "b");
        Assert.Equal(5, a.Files);
        Assert.Equal(3, a.Tests);
        Assert.Equal(0, b.Tests);
        Assert.Equal(150, b.Bytes);
        Assert.Equal("a/b", b.Path);
        Assert.Equal(["b", "big.gd", "tests", "z.png"], a.Children!.Select(c => c.Name));
    }
}

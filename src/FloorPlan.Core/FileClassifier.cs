namespace FloorPlan.Core;

/// <summary>Assigns a kind from the file name. Unknown extensions are decided by content later.</summary>
public static class FileClassifier
{
    static readonly Dictionary<string, FileKind> Extensions = Build(new()
    {
        [FileKind.Code] =
        [
            ".gd", ".cs", ".py", ".gdshader", ".gdshaderinc", ".shader", ".hlsl", ".glsl", ".vert", ".frag", ".comp", ".wgsl",
            ".c", ".h", ".cc", ".cpp", ".cxx", ".hh", ".hpp", ".hxx", ".inl", ".m", ".mm", ".rs", ".go", ".java", ".kt", ".kts",
            ".swift", ".dart", ".js", ".jsx", ".mjs", ".cjs", ".ts", ".tsx", ".mts", ".cts", ".vue", ".svelte", ".lua", ".rb",
            ".php", ".sh", ".bash", ".ps1", ".psm1", ".bat", ".cmd", ".sql", ".asm", ".s", ".zig", ".fs", ".vb", ".r", ".jl",
            ".scala", ".ex", ".exs", ".erl", ".clj", ".hs", ".ml", ".pl", ".html", ".htm", ".css", ".scss", ".sass", ".less",
        ],
        [FileKind.Docs] = [".md", ".markdown", ".txt", ".rst", ".adoc", ".org"],
        [FileKind.Data] =
        [
            ".json", ".jsonc", ".yaml", ".yml", ".toml", ".xml", ".csv", ".tsv", ".ini", ".cfg", ".conf", ".env", ".tscn",
            ".tres", ".godot", ".import", ".uid", ".gdextension", ".csproj", ".fsproj", ".vbproj", ".sln", ".slnx", ".props",
            ".targets", ".gradle", ".lock", ".editorconfig", ".gitignore", ".gitattributes", ".gitmodules", ".plist",
        ],
        [FileKind.Asset] =
        [
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", ".webp", ".psd", ".kra", ".exr", ".hdr", ".ktx", ".ktx2", ".dds",
            ".svg", ".ico", ".icns", ".glb", ".gltf", ".fbx", ".obj", ".mtl", ".blend", ".blend1", ".dae", ".wav", ".ogg",
            ".mp3", ".flac", ".opus", ".ttf", ".otf", ".woff", ".woff2", ".fnt", ".mp4", ".webm", ".avi", ".mov", ".pdf",
            ".zip", ".7z", ".gz", ".tar", ".dll", ".exe", ".so", ".dylib", ".bin", ".pck", ".res", ".scn", ".mesh", ".ctex",
            ".stex", ".lmbake", ".jar", ".aseprite", ".ase", ".xcf", ".pdn", ".pcf",
        ],
    });

    static readonly Dictionary<string, FileKind> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CMakeLists.txt"] = FileKind.Code,
        ["Makefile"] = FileKind.Code,
        ["Dockerfile"] = FileKind.Code,
        ["LICENSE"] = FileKind.Docs,
        ["COPYING"] = FileKind.Docs,
        ["README"] = FileKind.Docs,
        ["CHANGELOG"] = FileKind.Docs,
    };

    static readonly HashSet<string> TestFolders = new(StringComparer.OrdinalIgnoreCase) { "test", "tests", "__tests__", "spec", "specs" };
    static readonly string[] TestSuffixes = ["_test", ".test", "-test", "_spec", ".spec", "-spec"];

    /// <summary>Returns the kind implied by the name, or null when content must decide.</summary>
    public static FileKind? FromName(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        if (Names.TryGetValue(name, out var named)) return named;
        var extension = System.IO.Path.GetExtension(name);
        if (extension.Length == 0 && name.StartsWith('.')) extension = name;
        if (!Extensions.TryGetValue(extension, out var kind)) return null;
        return kind == FileKind.Code && IsTestPath(path) ? FileKind.Test : kind;
    }

    /// <summary>
    /// Whether common conventions mark a code file as a test: a folder named test, tests, __tests__, spec or specs,
    /// or a name such as test_x.py, x_test.go, x.test.ts, x.spec.js, XTest.cs or XTests.cs.
    /// </summary>
    public static bool IsTestPath(string path)
    {
        var parts = path.Split('/');
        for (int i = 0; i < parts.Length - 1; i++)
            if (TestFolders.Contains(parts[i])) return true;
        var stem = System.IO.Path.GetFileNameWithoutExtension(parts[^1]);
        if (stem.Equals("test", StringComparison.OrdinalIgnoreCase) || stem.Equals("tests", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("conftest", StringComparison.OrdinalIgnoreCase)) return true;
        if (stem.StartsWith("test_", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var suffix in TestSuffixes)
            if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
        // PascalCase only, so that "latest" or "contest" do not count.
        return stem.Length > 4 && (stem.EndsWith("Test", StringComparison.Ordinal) || stem.EndsWith("Tests", StringComparison.Ordinal));
    }

    /// <summary>Kinds whose physical lines are counted.</summary>
    public static bool CountsLines(FileKind kind) => kind is FileKind.Code or FileKind.Test or FileKind.Docs or FileKind.Data;

    /// <summary>Kinds whose lines make up the Lines area: code, tests and documentation.</summary>
    public static bool IsLinesArea(FileKind kind) => kind is FileKind.Code or FileKind.Test or FileKind.Docs;

    static Dictionary<string, FileKind> Build(Dictionary<FileKind, string[]> groups)
    {
        var map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, extensions) in groups)
            foreach (var extension in extensions) map[extension] = kind;
        return map;
    }
}

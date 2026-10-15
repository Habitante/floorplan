using System.Diagnostics.CodeAnalysis;

namespace FloorPlan.Core;

/// <summary>
/// The files and folders of one scan by project-relative path. Paths that arrive from the front
/// end or from agents resolve only to entries of this index, so they can never name anything
/// outside the scanned inventory.
/// </summary>
public sealed class ProjectIndex
{
    readonly Dictionary<string, TreeNode> byPath = new(StringComparer.Ordinal);
    /// <summary>Case-insensitive lookup; null where two entries differ only by case.</summary>
    readonly Dictionary<string, TreeNode?> byFoldedPath = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<int, TreeNode> byId = [];
    readonly HashSet<string> paths = new(StringComparer.Ordinal);
    readonly string prefix;

    public ProjectIndex(ProjectScan scan)
    {
        Scan = scan;
        prefix = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
        Add(scan.Tree);
    }

    public ProjectScan Scan { get; }
    public string Root => Scan.Project.Root;
    public TreeNode Tree => Scan.Tree;

    /// <summary>Every file and folder path below the root.</summary>
    public IReadOnlySet<string> Paths => paths;

    void Add(TreeNode node)
    {
        byPath[node.Path] = node;
        byFoldedPath[node.Path] = byFoldedPath.ContainsKey(node.Path) ? null : node;
        if (node.Path.Length > 0) paths.Add(node.Path);
        if (node.Id is int id) byId[id] = node;
        foreach (var child in node.Children ?? []) Add(child);
    }

    /// <summary>The entry at an exact project-relative path, as the front end sends them.</summary>
    public TreeNode? Get(string? path) => path is not null && byPath.TryGetValue(path, out var node) ? node : null;

    public TreeNode? File(int id) => byId.GetValueOrDefault(id);

    /// <summary>A file of the scan and its location on disk, for a path from the front end.</summary>
    public bool TryGetFile(string? path, [NotNullWhen(true)] out string? fullPath)
    {
        fullPath = null;
        if (Get(path) is not { IsFile: true } file) return false;
        var full = Path.GetFullPath(Path.Combine(Root, file.Path.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        fullPath = full;
        return true;
    }

    /// <summary>
    /// Resolves a path written by an agent: absolute, relative to its working folder or relative
    /// to the project root, with either slash. Case is ignored only where that is unambiguous.
    /// "", "." and "/" name the whole project.
    /// </summary>
    public TreeNode? Find(string? path, string? workingFolder = null)
    {
        if (path is null) return null;
        var text = path.Trim();
        if (text is "" or "." or "/" or "\\") return Tree;

        // "/scenes/world.gd" is rooted on the current drive; agents mean the project root.
        var relative = !Path.IsPathFullyQualified(text) && Path.IsPathRooted(text) ? text.TrimStart('/', '\\') : text;
        foreach (var folder in workingFolder is null ? [Root] : new[] { workingFolder, Root })
        {
            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(folder, relative));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
            if (RelativePath(full) is { } inside && Lookup(inside) is { } node) return node;
        }
        // Names that full-path normalisation would alter, such as a trailing dot.
        return Lookup(relative.Replace('\\', '/').Trim('/'));
    }

    TreeNode? Lookup(string relative) =>
        byPath.TryGetValue(relative, out var exact) ? exact : byFoldedPath.GetValueOrDefault(relative);

    /// <summary>The project-relative path of a full path, or null outside the project.</summary>
    string? RelativePath(string full)
    {
        full = Path.TrimEndingDirectorySeparator(full);
        if (full.Equals(Path.TrimEndingDirectorySeparator(Root), StringComparison.OrdinalIgnoreCase)) return "";
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        return full[prefix.Length..].Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Every file at or below a node.</summary>
    public static IEnumerable<TreeNode> FilesUnder(TreeNode node) =>
        node.IsFile ? [node] : (node.Children ?? []).SelectMany(FilesUnder);
}

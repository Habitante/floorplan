namespace FloorPlan.Core;

public sealed record FileRecord(
    int Id,
    string Path,
    FileKind Kind,
    EntryStatus Status,
    long? Bytes,
    int? Lines,
    DateTimeOffset? Modified,
    string? Note,
    WorkingChange? Change = null);

/// <summary>Builds the folder tree and its totals. Each file is counted once, in every ancestor.</summary>
public static class TreeBuilder
{
    public static TreeNode Build(string rootName, IEnumerable<FileRecord> files)
    {
        var root = NewFolder(rootName, "");
        var folders = new Dictionary<string, TreeNode>(StringComparer.Ordinal) { [""] = root };

        foreach (var file in files)
        {
            int slash = file.Path.LastIndexOf('/');
            var parent = slash < 0 ? root : GetFolder(folders, file.Path[..slash]);
            parent.Children!.Add(new TreeNode
            {
                Name = file.Path[(slash + 1)..],
                Path = file.Path,
                Id = file.Id,
                Kind = file.Kind,
                Status = file.Status,
                Bytes = file.Bytes,
                Lines = file.Lines,
                Modified = file.Modified,
                Note = file.Note,
                Change = file.Change,
            });
        }

        Aggregate(root);
        return root;
    }

    static TreeNode GetFolder(Dictionary<string, TreeNode> folders, string path)
    {
        if (folders.TryGetValue(path, out var existing)) return existing;
        int slash = path.LastIndexOf('/');
        var parent = slash < 0 ? folders[""] : GetFolder(folders, path[..slash]);
        var folder = NewFolder(path[(slash + 1)..], path);
        parent.Children!.Add(folder);
        folders[path] = folder;
        return folder;
    }

    static TreeNode NewFolder(string name, string path) => new() { Name = name, Path = path, Children = [] };

    static void Aggregate(TreeNode folder)
    {
        long bytes = 0, code = 0, docs = 0, data = 0;
        int files = 0, unmeasured = 0;
        DateTimeOffset? modified = null;

        folder.Children!.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (var child in folder.Children)
        {
            if (child.IsFile)
            {
                files++;
                bytes += child.Bytes ?? 0;
                bool linesExpected = child.Kind is { } kind && FileClassifier.CountsLines(kind) && child.Status == EntryStatus.Ok;
                if (child.Bytes is null || (linesExpected && child.Lines is null)) unmeasured++;
                switch (child.Kind)
                {
                    case FileKind.Code: code += child.Lines ?? 0; break;
                    case FileKind.Docs: docs += child.Lines ?? 0; break;
                    case FileKind.Data: data += child.Lines ?? 0; break;
                }
            }
            else
            {
                Aggregate(child);
                files += child.Files ?? 0;
                bytes += child.Bytes ?? 0;
                code += child.Code ?? 0;
                docs += child.Docs ?? 0;
                data += child.Data ?? 0;
                unmeasured += child.Unmeasured ?? 0;
            }
            if (child.Modified is { } m && (modified is null || m > modified)) modified = m;
        }

        folder.Files = files;
        folder.Bytes = bytes;
        folder.Code = code;
        folder.Docs = docs;
        folder.Data = data;
        folder.Unmeasured = unmeasured;
        folder.Modified = modified;
    }
}

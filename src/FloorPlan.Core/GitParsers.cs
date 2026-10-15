using System.Text;

namespace FloorPlan.Core;

public sealed record TrackedPath(string Path, string Mode);

public sealed record RawCommit(string Sha, long Time, string Author, string Subject, IReadOnlyList<string> Paths);

/// <summary>Parsers for NUL-delimited Git output. Paths are decoded as UTF-8 and never unquoted.</summary>
public static class GitParsers
{
    public const string LogFormat = "%x1e%H%x1f%ct%x1f%an%x1f%s";

    /// <summary>Parses <c>git ls-files -z -s</c>: "mode SP object SP stage TAB path NUL".</summary>
    public static List<TrackedPath> ParseLsFiles(ReadOnlySpan<byte> output)
    {
        var result = new List<TrackedPath>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (!output.IsEmpty)
        {
            int end = output.IndexOf((byte)0);
            var record = end < 0 ? output : output[..end];
            output = end < 0 ? [] : output[(end + 1)..];
            int tab = record.IndexOf((byte)'\t');
            if (tab < 0) continue;
            int space = record.IndexOf((byte)' ');
            string mode = space > 0 ? Encoding.ASCII.GetString(record[..space]) : "";
            string path = Encoding.UTF8.GetString(record[(tab + 1)..]);
            // Unmerged paths appear once per conflict stage.
            if (seen.Add(path)) result.Add(new TrackedPath(path, mode));
        }
        return result;
    }

    /// <summary>
    /// Parses <c>git status --porcelain=v1 -z</c>: "XY SP path NUL", followed by the original
    /// path and another NUL for renames and copies. Paths are relative to the repository root.
    /// </summary>
    public static Dictionary<string, WorkingChange> ParseStatus(ReadOnlySpan<byte> output)
    {
        var changes = new Dictionary<string, WorkingChange>(StringComparer.Ordinal);
        var fields = new List<string>();
        foreach (var range in output.Split((byte)0))
            if (!output[range].IsEmpty) fields.Add(Encoding.UTF8.GetString(output[range]));

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            if (field.Length < 4) continue;
            char x = field[0], y = field[1];
            string path = field[3..];
            if (x is 'R' or 'C') i++; // the original path follows; it is no longer in the working tree
            changes[path] = (x, y) switch
            {
                ('?', '?') => WorkingChange.Untracked,
                _ when x == 'D' || y == 'D' => WorkingChange.Deleted,
                _ when x == 'A' || y == 'A' => WorkingChange.Added,
                _ when x is 'R' or 'C' => WorkingChange.Renamed,
                _ => WorkingChange.Modified,
            };
        }
        return changes;
    }

    /// <summary>
    /// Parses <c>git log -z --name-only --format=<see cref="LogFormat"/></c>. Each commit is
    /// RS + header + NUL, then optionally LF and NUL-terminated paths.
    /// </summary>
    public static List<RawCommit> ParseLog(ReadOnlySpan<byte> output)
    {
        var commits = new List<RawCommit>();
        foreach (var range in output.Split((byte)0x1e))
        {
            var chunk = output[range];
            if (chunk.IsEmpty) continue;
            int headerEnd = chunk.IndexOf((byte)0);
            var header = headerEnd < 0 ? chunk : chunk[..headerEnd];
            var fields = Encoding.UTF8.GetString(header).Split('\x1f', 4);
            if (fields.Length < 4 || !long.TryParse(fields[1], out long time)) continue;

            var paths = new List<string>();
            if (headerEnd >= 0)
            {
                var rest = chunk[(headerEnd + 1)..];
                if (!rest.IsEmpty && rest[0] == (byte)'\n') rest = rest[1..];
                foreach (var pathRange in rest.Split((byte)0))
                {
                    var path = rest[pathRange];
                    if (!path.IsEmpty) paths.Add(Encoding.UTF8.GetString(path));
                }
            }
            commits.Add(new RawCommit(fields[0], time, fields[2], fields[3], paths));
        }
        return commits;
    }
}

using System.Runtime.CompilerServices;

namespace FloorPlan.Core;

/// <summary>A file that changes in the same commits as another, with its evidence.</summary>
/// <param name="Together">Loaded commits that touched both files.</param>
/// <param name="Commits">Loaded commits that touched this partner.</param>
/// <param name="Strength">
/// From 0 to 1, how much of the two files' histories they share, with broad commits counting for
/// less; a file that changes in nearly every commit ranks low with each of them.
/// </param>
/// <param name="Shared">The shared commits, newest first, at most a few.</param>
public sealed record Partner(string Path, int Together, int Commits, double Strength, IReadOnlyList<string> Shared);

/// <summary>What changes with a file in the loaded history, and the files its name pairs it with.</summary>
/// <param name="Commits">Loaded commits that touched the file.</param>
/// <param name="ChangesWith">Its partners, strongest first.</param>
/// <param name="PairedByName">
/// For code, its tests by the naming conventions; for a test, the code it is named for. Pairing is
/// not coverage.
/// </param>
public sealed record FileRelations(string Path, int Commits, IReadOnlyList<Partner> ChangesWith, IReadOnlyList<string> PairedByName);

/// <summary>
/// Files that change together: "changes with", never "depends on". Read from the whole loaded
/// history rather than the human's range, since a relation needs evidence. Agents' commits are
/// often broad, so a commit counts for less the more files it touches, and a pair needs enough
/// shared commits to count.
/// </summary>
public static class ChangesWith
{
    /// <summary>A commit relating a file to more than this many others counts for proportionally less.</summary>
    public const int Broad = 10;
    /// <summary>A pair needs this many shared commits...</summary>
    public const int MinTogether = 2;
    /// <summary>...worth at least one commit once broad ones are discounted.</summary>
    public const double MinWeight = 1;
    public const int Limit = 12;
    const int SharedListed = 5;

    /// <summary>Folder names that say where tests live rather than what they test.</summary>
    static readonly HashSet<string> GenericFolders = new(StringComparer.OrdinalIgnoreCase) { "test", "tests", "__tests__", "spec", "specs", "src", "lib" };

    static readonly ConditionalWeakTable<ProjectIndex, History> Histories = [];

    /// <summary>How much a commit counts: fully up to <see cref="Broad"/> other files, then in proportion.</summary>
    public static double Weight(CommitInfo commit)
    {
        int others = commit.Files.Length + commit.Other - 1;
        return others <= Broad ? 1 : (double)Broad / others;
    }

    /// <summary>What changes with a file and the files its name pairs it with; null for a folder.</summary>
    public static FileRelations? For(ProjectIndex index, TreeNode file, int limit = Limit)
    {
        if (file.Id is not int id) return null;
        var history = Histories.GetValue(index, i => new History(i));
        var commits = index.Scan.Commits;
        var own = history.CommitsOf.GetValueOrDefault(id) ?? [];

        var shared = new Dictionary<int, Shared>();
        foreach (int c in own)
        {
            double weight = history.Weights[c];
            foreach (int other in commits[c].Files)
            {
                if (other == id) continue;
                if (!shared.TryGetValue(other, out var pair)) shared[other] = pair = new Shared();
                pair.Weight += weight;
                pair.Commits.Add(c);
            }
        }

        double mine = history.WeightOf.GetValueOrDefault(id);
        var partners = shared
            .Where(p => p.Value.Commits.Count >= MinTogether && p.Value.Weight >= MinWeight && index.File(p.Key) is not null)
            .Select(p => (Id: p.Key, p.Value, Strength: p.Value.Weight / Math.Sqrt(mine * history.WeightOf[p.Key])))
            .OrderByDescending(p => p.Strength)
            .ThenByDescending(p => p.Value.Commits.Count)
            .ThenBy(p => index.File(p.Id)!.Path, StringComparer.Ordinal)
            .Take(limit)
            .Select(p => new Partner(
                index.File(p.Id)!.Path,
                p.Value.Commits.Count,
                history.CommitsOf[p.Id].Count,
                Math.Round(Math.Min(1, p.Strength), 2),
                p.Value.Commits.Take(SharedListed).Select(c => BoardReports.Short(commits[c].Sha)).ToList()))
            .ToList();
        return new FileRelations(file.Path, own.Count, partners, PairedByName(history, file, limit));
    }

    /// <summary>
    /// For code, the tests named for it; for a test, the code it is named for. Where several files
    /// share the name, those sharing most folder names with this one, as material/text_field.dart
    /// and test/material/text_field_test.dart do.
    /// </summary>
    static IReadOnlyList<string> PairedByName(History history, TreeNode file, int limit)
    {
        var extension = Path.GetExtension(file.Name);
        var candidates = file.Kind switch
        {
            FileKind.Test => FileClassifier.TestedName(file.Name) is { } named ? history.CodeByName.GetValueOrDefault(Key(named, extension)) : null,
            FileKind.Code => history.TestsByName.GetValueOrDefault(Key(Path.GetFileNameWithoutExtension(file.Name), extension)),
            _ => null,
        };
        if (candidates is not { Count: > 0 }) return [];
        var mine = Folders(file.Path);
        int Score(TreeNode n) => Folders(n.Path).Count(mine.Contains);
        int best = candidates.Max(Score);
        return candidates.Where(n => Score(n) == best).Select(n => n.Path).Order(StringComparer.Ordinal).Take(limit).ToList();
    }

    static HashSet<string> Folders(string path)
    {
        var parts = path.Split('/');
        return new HashSet<string>(parts[..^1].Where(p => !GenericFolders.Contains(p)), StringComparer.OrdinalIgnoreCase);
    }

    static string Key(string name, string extension) => $"{name}|{extension}";

    sealed class Shared
    {
        public double Weight;
        /// <summary>Indices into the scan's commits, newest first.</summary>
        public readonly List<int> Commits = [];
    }

    /// <summary>Per scan, built on first use: each file's commits and their weight, and files by name.</summary>
    sealed class History
    {
        public readonly double[] Weights;
        /// <summary>Indices into the scan's commits, newest first.</summary>
        public readonly Dictionary<int, List<int>> CommitsOf = [];
        public readonly Dictionary<int, double> WeightOf = [];
        public readonly Dictionary<string, List<TreeNode>> CodeByName = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<TreeNode>> TestsByName = new(StringComparer.OrdinalIgnoreCase);

        public History(ProjectIndex index)
        {
            var commits = index.Scan.Commits;
            Weights = new double[commits.Count];
            for (int c = 0; c < commits.Count; c++)
            {
                double weight = Weights[c] = Weight(commits[c]);
                foreach (int id in commits[c].Files)
                {
                    if (!CommitsOf.TryGetValue(id, out var list)) CommitsOf[id] = list = [];
                    list.Add(c);
                    WeightOf[id] = WeightOf.GetValueOrDefault(id) + weight;
                }
            }
            Add(index.Tree);
        }

        void Add(TreeNode node)
        {
            if (node.IsFile)
            {
                var extension = Path.GetExtension(node.Name);
                if (node.Kind == FileKind.Code) Append(CodeByName, Key(Path.GetFileNameWithoutExtension(node.Name), extension), node);
                else if (node.Kind == FileKind.Test && FileClassifier.TestedName(node.Name) is { } named) Append(TestsByName, Key(named, extension), node);
            }
            foreach (var child in node.Children ?? []) Add(child);
        }

        static void Append(Dictionary<string, List<TreeNode>> map, string key, TreeNode node)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = [];
            list.Add(node);
        }
    }
}

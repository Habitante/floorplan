using System.Diagnostics;

namespace FloorPlan.Core;

/// <summary>
/// Inventories a project folder and reads its Git history. Read-only: nothing is written
/// to the project, and Git runs with optional locks and repository-configured helpers off.
/// </summary>
public sealed class ProjectScanner(GitRunner git)
{
    sealed record Candidate(string Path, EntryStatus? Boundary);

    public async Task<ProjectScan> ScanAsync(
        string path,
        ScanOptions? options = null,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ScanOptions();
        var clock = Stopwatch.StartNew();
        var scannedAt = DateTimeOffset.UtcNow;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Folder not found: {root}");

        var issues = new List<ScanIssue>();
        var repository = await DetectRepositoryAsync(root, issues, cancellationToken);

        List<Candidate> candidates;
        var changes = new Dictionary<string, WorkingChange>(StringComparer.Ordinal);
        int untrackedHidden = 0;
        if (repository is not null)
        {
            var listing = await git.RunCheckedAsync(root, ["ls-files", "-z", "-s"], cancellationToken);
            candidates = GitParsers.ParseLsFiles(listing.Output)
                .Select(t => new Candidate(t.Path, t.Mode switch
                {
                    "160000" => EntryStatus.Repository,
                    "120000" => EntryStatus.Link,
                    _ => null,
                }))
                .ToList();

            changes = await ReadChangesAsync(root, repository.Prefix, cancellationToken);
            var untracked = changes.Where(c => c.Value == WorkingChange.Untracked).Select(c => c.Key).ToList();
            if (options.IncludeUntracked)
            {
                // A trailing slash marks an untracked nested repository.
                candidates.AddRange(untracked.Select(p => p.EndsWith('/') ? new Candidate(p.TrimEnd('/'), EntryStatus.Repository) : new Candidate(p, null)));
                candidates.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
            }
            else
            {
                untrackedHidden = untracked.Count;
            }
        }
        else
        {
            candidates = EnumerateFolder(root, options, issues, cancellationToken);
        }

        var records = await MeasureAsync(root, candidates, options, progress, issues, cancellationToken);
        for (int i = 0; i < records.Length; i++)
        {
            var recordPath = records[i].Path;
            if (changes.TryGetValue(recordPath, out var change) || changes.TryGetValue(recordPath + "/", out change))
                records[i] = records[i] with { Change = change };
        }

        IReadOnlyList<CommitInfo> commits = [];
        bool historyComplete = repository is null;
        if (repository is { Head: not null })
        {
            progress?.Report(new ScanProgress("history", 0, 0));
            var raw = await ReadHistoryAsync(root, repository.Prefix, options.CommitLimit, cancellationToken);
            commits = MapCommits(raw, records);
            historyComplete = raw.Count < options.CommitLimit;
        }

        var tree = TreeBuilder.Build(Path.GetFileName(root) is { Length: > 0 } name ? name : root, records);
        return new ProjectScan
        {
            Project = new ProjectInfo
            {
                Root = root,
                Name = tree.Name,
                Scope = repository is null ? "folder" : options.IncludeUntracked ? "tracked+untracked" : "tracked",
                Git = repository is not null,
                Head = repository?.Head,
                Branch = repository?.Branch,
                GitDirectory = repository?.GitDirectory,
                CommitLimit = options.CommitLimit,
                HistoryComplete = historyComplete,
                Changes = changes.Count,
                UntrackedHidden = untrackedHidden,
                ScannedAt = scannedAt,
                DurationMs = clock.ElapsedMilliseconds,
            },
            Tree = tree,
            Commits = commits,
            Issues = issues,
        };
    }

    sealed record Repository(string Prefix, string? Head, string? Branch, string GitDirectory);

    /// <summary>Uncommitted changes under the scanned folder, keyed by scan-relative path.</summary>
    async Task<Dictionary<string, WorkingChange>> ReadChangesAsync(string root, string prefix, CancellationToken cancellationToken)
    {
        List<string> arguments = [GitRunner.LiteralPathspecs, "status", "--porcelain=v1", "-z", "--untracked-files=all"];
        if (prefix.Length > 0) arguments.AddRange(["--", "."]);
        var status = await git.RunCheckedAsync(root, arguments, cancellationToken);
        var changes = new Dictionary<string, WorkingChange>(StringComparer.Ordinal);
        foreach (var (path, change) in GitParsers.ParseStatus(status.Output))
            if (path.StartsWith(prefix, StringComparison.Ordinal)) changes[path[prefix.Length..]] = change;
        return changes;
    }

    async Task<Repository?> DetectRepositoryAsync(string root, List<ScanIssue> issues, CancellationToken cancellationToken)
    {
        GitResult inside;
        try
        {
            inside = await git.RunAsync(root, ["rev-parse", "--is-inside-work-tree"], cancellationToken);
        }
        catch (GitUnavailableException e)
        {
            issues.Add(new ScanIssue(null, $"{e.Message}; showing the folder without history"));
            return null;
        }
        if (inside.ExitCode != 0 || inside.Text != "true")
        {
            if (inside.ExitCode != 0 && !inside.Error.Contains("not a git repository", StringComparison.OrdinalIgnoreCase))
                issues.Add(new ScanIssue(null, $"Git: {inside.Error}"));
            return null;
        }

        var prefix = (await git.RunCheckedAsync(root, ["rev-parse", "--show-prefix"], cancellationToken)).Text;
        var gitDirectory = (await git.RunCheckedAsync(root, ["rev-parse", "--absolute-git-dir"], cancellationToken)).Text;
        var head = await git.RunAsync(root, ["rev-parse", "--verify", "-q", "HEAD"], cancellationToken);
        var branch = await git.RunAsync(root, ["symbolic-ref", "--short", "-q", "HEAD"], cancellationToken);
        return new Repository(
            prefix,
            head.ExitCode == 0 ? head.Text : null,
            branch.ExitCode == 0 ? branch.Text : null,
            Path.GetFullPath(gitDirectory));
    }

    async Task<List<RawCommit>> ReadHistoryAsync(string root, string prefix, int limit, CancellationToken cancellationToken)
    {
        List<string> arguments =
        [
            GitRunner.LiteralPathspecs, "log", "-z", "--no-merges", "--no-renames", "--name-only",
            $"--max-count={limit}", $"--format={GitParsers.LogFormat}",
        ];
        // From a subfolder, keep paths relative to it and only commits that touch it.
        if (prefix.Length > 0) arguments.AddRange(["--relative", "--", "."]);
        var result = await git.RunCheckedAsync(root, arguments, cancellationToken);
        return GitParsers.ParseLog(result.Output);
    }

    static List<CommitInfo> MapCommits(List<RawCommit> raw, IReadOnlyList<FileRecord> records)
    {
        var ids = records.ToDictionary(r => r.Path, r => r.Id, StringComparer.Ordinal);
        return raw.Select(c =>
        {
            var files = new List<int>(c.Paths.Count);
            int other = 0;
            foreach (var p in c.Paths)
                if (ids.TryGetValue(p, out int id)) files.Add(id); else other++;
            return new CommitInfo { Sha = c.Sha, Time = c.Time, Author = c.Author, Subject = c.Subject, Files = [.. files], Other = other };
        }).ToList();
    }

    static List<Candidate> EnumerateFolder(string root, ScanOptions options, List<ScanIssue> issues, CancellationToken cancellationToken)
    {
        var found = new List<Candidate>();
        var pending = new Stack<(string Full, string Relative)>();
        pending.Push((root, ""));
        var enumeration = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (full, relative) = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(full).EnumerateFileSystemInfos("*", enumeration).ToList();
            }
            catch (Exception e) when (e is UnauthorizedAccessException or IOException)
            {
                issues.Add(new ScanIssue(relative, e.Message));
                continue;
            }

            foreach (var entry in entries)
            {
                var path = relative.Length == 0 ? entry.Name : $"{relative}/{entry.Name}";
                bool isFolder = entry.Attributes.HasFlag(FileAttributes.Directory);
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    found.Add(new Candidate(path, EntryStatus.Link));
                else if (!isFolder)
                    found.Add(new Candidate(path, null));
                else if (options.FolderExclusions.Contains(entry.Name))
                    continue;
                else if (Directory.Exists(Path.Combine(entry.FullName, ".git")))
                    found.Add(new Candidate(path, EntryStatus.Repository));
                else
                    pending.Push((entry.FullName, path));
            }
        }

        found.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return found;
    }

    static async Task<FileRecord[]> MeasureAsync(
        string root,
        List<Candidate> candidates,
        ScanOptions options,
        IProgress<ScanProgress>? progress,
        List<ScanIssue> issues,
        CancellationToken cancellationToken)
    {
        var records = new FileRecord[candidates.Count];
        int done = 0;
        progress?.Report(new ScanProgress("files", 0, candidates.Count));

        await Parallel.ForEachAsync(
            Enumerable.Range(0, candidates.Count),
            new ParallelOptions { MaxDegreeOfParallelism = options.Concurrency, CancellationToken = cancellationToken },
            async (i, token) =>
            {
                records[i] = await MeasureOneAsync(root, i, candidates[i], options, token);
                int n = Interlocked.Increment(ref done);
                if (n % 200 == 0) progress?.Report(new ScanProgress("files", n, candidates.Count));
            });

        progress?.Report(new ScanProgress("files", candidates.Count, candidates.Count));
        foreach (var r in records)
            if (r.Status == EntryStatus.Unreadable) issues.Add(new ScanIssue(r.Path, r.Note ?? "Unreadable"));
        return records;
    }

    static async Task<FileRecord> MeasureOneAsync(string root, int id, Candidate candidate, ScanOptions options, CancellationToken cancellationToken)
    {
        var path = candidate.Path;
        var kind = FileClassifier.FromName(path);
        if (candidate.Boundary is { } boundary)
        {
            var note = boundary == EntryStatus.Link ? "Link, not followed" : "Nested repository, not traversed";
            return new FileRecord(id, path, kind ?? FileKind.Other, boundary, null, null, null, note);
        }

        var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists)
            {
                return Directory.Exists(full)
                    ? new FileRecord(id, path, FileKind.Other, EntryStatus.Repository, null, null, null, "Folder in place of a tracked file")
                    : new FileRecord(id, path, kind ?? FileKind.Other, EntryStatus.Missing, null, null, null, "Not in the working tree");
            }
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return new FileRecord(id, path, kind ?? FileKind.Other, EntryStatus.Link, null, null, null, "Link, not followed");

            long bytes = info.Length;
            var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

            if (kind is FileKind.Asset)
                return new FileRecord(id, path, FileKind.Asset, EntryStatus.Ok, bytes, null, modified, null);

            if (kind is { } known && FileClassifier.CountsLines(known))
            {
                if (bytes > options.MaxLineCountBytes)
                    return new FileRecord(id, path, known, EntryStatus.Ok, bytes, null, modified, "Too large to count lines");
                var content = await LineCounter.ReadAsync(full, (int)bytes, cancellationToken);
                int? lines = LineCounter.Count(content);
                return new FileRecord(id, path, known, EntryStatus.Ok, bytes, lines, modified, lines is null ? "Binary content" : null);
            }

            // Unknown name: only the content decides between binary and unclassified text.
            var probe = await LineCounter.ReadAsync(full, LineCounter.BinaryProbeBytes, cancellationToken);
            var decided = LineCounter.LooksBinary(probe) ? FileKind.Asset : FileKind.Other;
            return new FileRecord(id, path, decided, EntryStatus.Ok, bytes, null, modified, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new FileRecord(id, path, kind ?? FileKind.Other, EntryStatus.Unreadable, null, null, null, e.Message);
        }
    }
}

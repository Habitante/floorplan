namespace FloorPlan.Core;

/// <summary>
/// Watches a project folder (and its Git directory when that lies outside it) and reports
/// batches of changed paths once the folder has been quiet for a moment. Paths are relative
/// to the project root with forward slashes; Git directory paths are reported under ".git/".
/// </summary>
public sealed class ProjectWatcher : IDisposable
{
    readonly List<FileSystemWatcher> watchers = [];
    readonly Lock gate = new();
    readonly HashSet<string> pending = new(StringComparer.Ordinal);
    readonly Func<IReadOnlyCollection<string>, bool, Task> onBatch;
    readonly TimeSpan quiet;
    readonly TimeSpan maxWait;
    readonly Timer timer;
    DateTime firstPending;
    bool overflow;
    bool disposed;

    /// <param name="onBatch">Receives the changed paths, and true when events were lost and everything may have changed.</param>
    public ProjectWatcher(string root, string? gitDirectory, Func<IReadOnlyCollection<string>, bool, Task> onBatch,
        TimeSpan? quiet = null, TimeSpan? maxWait = null)
    {
        this.onBatch = onBatch;
        this.quiet = quiet ?? TimeSpan.FromMilliseconds(400);
        this.maxWait = maxWait ?? TimeSpan.FromSeconds(2);
        timer = new Timer(_ => Flush());

        Watch(root, "");
        var rootGit = Path.Combine(root, ".git");
        if (gitDirectory is not null && !string.Equals(Path.GetFullPath(gitDirectory), Path.GetFullPath(rootGit), StringComparison.OrdinalIgnoreCase))
            Watch(gitDirectory, ".git/");
    }

    void Watch(string folder, string prefix)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        FileSystemEventHandler changed = (_, e) => Add(prefix, e.Name);
        watcher.Created += changed;
        watcher.Changed += changed;
        watcher.Deleted += changed;
        watcher.Renamed += (_, e) =>
        {
            Add(prefix, e.OldName);
            Add(prefix, e.Name);
        };
        watcher.Error += (_, _) =>
        {
            lock (gate) overflow = true;
            Schedule();
        };
        watcher.EnableRaisingEvents = true;
        watchers.Add(watcher);
    }

    void Add(string prefix, string? name)
    {
        if (name is null) return;
        lock (gate)
        {
            if (pending.Count == 0) firstPending = DateTime.UtcNow;
            pending.Add(prefix + name.Replace('\\', '/'));
        }
        Schedule();
    }

    void Schedule()
    {
        lock (gate)
        {
            if (disposed) return;
            var waited = DateTime.UtcNow - firstPending;
            timer.Change(waited >= maxWait ? TimeSpan.Zero : quiet, Timeout.InfiniteTimeSpan);
        }
    }

    void Flush()
    {
        string[] batch;
        bool lost;
        lock (gate)
        {
            if (disposed || (pending.Count == 0 && !overflow)) return;
            batch = [.. pending];
            lost = overflow;
            pending.Clear();
            overflow = false;
        }
        _ = onBatch(batch, lost);
    }

    public void Dispose()
    {
        lock (gate) disposed = true;
        timer.Dispose();
        foreach (var watcher in watchers) watcher.Dispose();
    }
}

/// <summary>Decides whether a batch of changed paths can affect the map.</summary>
public static class ChangeRelevance
{
    /// <param name="known">Paths of files and folders in the last scan.</param>
    public static async Task<bool> AnyRelevantAsync(
        GitRunner git, string root, bool isGit, IReadOnlyCollection<string> paths, IReadOnlySet<string> known,
        IReadOnlySet<string> folderExclusions, CancellationToken cancellationToken)
    {
        var unknown = new List<string>();
        foreach (var path in paths)
        {
            if (path == ".git" || path.StartsWith(".git/", StringComparison.Ordinal))
            {
                // Commits, checkouts and resets move HEAD or refs. The index is rewritten by
                // editors' Git integrations all the time, so it is not a signal on its own.
                var inner = path.Length > 5 ? path[5..] : "";
                if (inner is "HEAD" or "packed-refs" || inner.StartsWith("refs/", StringComparison.Ordinal)) return true;
                continue;
            }
            if (known.Contains(path)) return true;
            if (!isGit)
            {
                if (!path.Split('/').Any(folderExclusions.Contains)) return true;
                continue;
            }
            unknown.Add(path);
        }
        if (unknown.Count == 0) return false;

        // A new or unknown path matters unless Git ignores it (build output, editor caches).
        foreach (var chunk in unknown.Chunk(100))
        {
            // Prints one ignored path per line; exit 0 when some are ignored, 1 when none are.
            var result = await git.RunAsync(root, ["check-ignore", "--", .. chunk], cancellationToken);
            if (result.ExitCode > 1) return true;
            int ignored = result.ExitCode == 0 ? result.Output.Count((byte)'\n') : 0;
            if (ignored < chunk.Length) return true;
        }
        return false;
    }
}

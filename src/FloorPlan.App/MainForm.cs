using System.Diagnostics;
using System.Text.Json;
using FloorPlan.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FloorPlan.App;

/// <summary>
/// The window: hosts the front end in WebView2, serves it from embedded resources and
/// answers a small set of messages (open, rescan). The front end has no filesystem access.
/// It also serves the shared board to agents through <c>floorplan mcp</c>.
/// </summary>
sealed class MainForm : Form, IBoardSurface
{
    const string Origin = "https://floorplan.invalid/";
    static readonly string? DevUrl = Environment.GetEnvironmentVariable("FLOORPLAN_DEV_URL");

    readonly LaunchOptions launch;
    readonly WebView2 web;
    readonly WebAssets assets = new();
    readonly GitRunner git = new();
    readonly ProjectScanner scanner;
    /// <summary>A last-seen HEAD given at launch, used instead of the stored one when the first project opens.</summary>
    string? seenAtLaunch;
    /// <summary>What this window last stored as seen.</summary>
    (string Root, string Head, DateTimeOffset At)? seenStored;
    CancellationTokenSource? scanCancellation;
    string? projectPath;
    ProjectScan? lastScan;
    bool scanning;
    bool rescanPending;
    ProjectWatcher? watcher;
    /// <summary>The project's description as last checked against the scan; null when it has none.</summary>
    DescriptionReport? description;
    FileSystemWatcher? descriptionWatcher;
    string? descriptionPath;
    string? watchedRoot;
    ProjectIndex? index;
    readonly BoardState board = new();
    BoardServer? boardServer;
    string? boardRoot;
    const int ReadLimit = 1024 * 1024;
    bool smokeDone;
    bool firstMapLogged;

    public int ExitCode { get; private set; }

    public MainForm(LaunchOptions launch)
    {
        this.launch = launch;
        scanner = new ProjectScanner(git);
        projectPath = launch.Path is { } p ? Path.GetFullPath(p) : AppData.LoadSettings().LastProject;
        seenAtLaunch = launch.Seen;
        Text = "FloorPlan";
        BackColor = Dark ? Color.FromArgb(0x0d, 0x0d, 0x0d) : Color.FromArgb(0xf9, 0xf9, 0xf7);
        StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = launch.Size ?? new Size((int)(area.Width * 0.8), (int)(area.Height * 0.85));
        MinimumSize = new Size(560, 420);
        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        Controls.Add(web);
    }

    /// <summary>The theme given at launch, otherwise the system's.</summary>
    bool Dark => launch.Theme is { } theme ? theme == "dark" : NativeMethods.AppsUseDarkTheme();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.UseDarkTitleBar(Handle, Dark);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Agents find the window by its own pipe, whichever project it shows.
        boardServer = new BoardServer(BoardChannel.PipeName(Environment.ProcessId),
            request => OnUiThread(() => BoardService.Handle(this, request, DateTimeOffset.Now)), AppData.Log);
        Activated += (_, _) => Present();
        try
        {
            await InitializeWebViewAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this, "FloorPlan needs the Microsoft Edge WebView2 Runtime, which is part of Windows 11.", "FloorPlan",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitCode = 1;
            Close();
        }
        if (launch.SmokeCapture is not null)
        {
            _ = Task.Delay(TimeSpan.FromSeconds(90) + launch.Settle).ContinueWith(_ => BeginInvoke(() =>
            {
                if (smokeDone) return;
                AppData.Log("smoke: timed out waiting for the first map");
                ExitCode = 1;
                Close();
            }), TaskScheduler.Default);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        scanCancellation?.Cancel();
        watcher?.Dispose();
        descriptionWatcher?.Dispose();
        _ = boardServer?.DisposeAsync();
        base.OnFormClosed(e);
    }

    async Task InitializeWebViewAsync()
    {
        // Windows sharing a WebView2 data folder share its browser and GPU processes, so a smoke
        // run uses its own: its measurements and captures stay independent of an open window.
        var environment = await CoreWebView2Environment.CreateAsync(null, launch.SmokeCapture is null ? AppData.WebView : AppData.SmokeWebView);
        await web.EnsureCoreWebView2Async(environment);
        var core = web.CoreWebView2;
        bool dev = DevUrl is not null || Debugger.IsAttached;
        core.Settings.AreDevToolsEnabled = dev;
        core.Settings.AreDefaultContextMenusEnabled = dev;
        core.Settings.AreBrowserAcceleratorKeysEnabled = dev;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;

        core.NavigationStarting += (_, args) => { if (!IsOwnUrl(args.Uri)) args.Cancel = true; };
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.WebMessageReceived += OnWebMessage;
        core.AddWebResourceRequestedFilter(Origin + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        var query = string.Join("&", new[] { launch.Theme is { } theme ? $"theme={theme}" : null, launch.Fill is { } fill ? $"fill={fill}" : null }.OfType<string>());
        core.Navigate((DevUrl ?? Origin + "index.html") + (query.Length > 0 ? "?" + query : ""));
    }

    static bool IsOwnUrl(string uri) =>
        uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase) ||
        (DevUrl is not null && uri.StartsWith(DevUrl, StringComparison.OrdinalIgnoreCase));

    void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var path = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath.TrimStart('/'));
        if (path.Length == 0) path = "index.html";

        Stream? body;
        string type;
        if (path == "user-fonts/icons")
        {
            var font = AppData.IconFont();
            body = font is null ? null : File.OpenRead(font);
            type = font is null ? "" : WebAssets.ContentType(font);
        }
        else
        {
            body = assets.Open(path);
            type = WebAssets.ContentType(path);
        }

        var environment = web.CoreWebView2.Environment;
        if (body is null)
        {
            e.Response = environment.CreateWebResourceResponse(null, 404, "Not Found", "");
            return;
        }
        var headers = $"Content-Type: {type}\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-cache";
        if (path == "index.html")
            headers += "\r\nContent-Security-Policy: default-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'";
        e.Response = environment.CreateWebResourceResponse(body, 200, "OK", headers);
    }

    void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!IsOwnUrl(e.Source)) return;
        using var message = JsonDocument.Parse(e.WebMessageAsJson);
        if (!message.RootElement.TryGetProperty("type", out var typeProperty)) return;
        switch (typeProperty.GetString())
        {
            case "ready":
                if (projectPath is not null) StartScan(projectPath);
                else Post(new { type = "idle" });
                break;
            case "open":
                ChooseFolder();
                break;
            case "rescan":
                if (projectPath is not null) StartScan(projectPath);
                break;
            case "scope":
                if (projectPath is { } scoped && message.RootElement.TryGetProperty("untracked", out var untracked))
                {
                    bool include = untracked.ValueKind == JsonValueKind.True;
                    AppData.UpdateSettings(s => s.For(scoped).HideUntracked = !include);
                    StartScan(scoped);
                }
                break;
            case "presence":
                Present();
                break;
            case "sinceDismissed":
                board.SinceLastSeen = null;
                break;
            case "rendered":
                OnRendered(message.RootElement);
                break;
            case "board":
                try
                {
                    if (message.RootElement.TryGetProperty("view", out var view) && view.Deserialize<BoardView>(ScanJson.Options) is { } boardView)
                        board.Update(boardView, lastScan?.Project.Head, DateTimeOffset.Now);
                }
                catch (JsonException error)
                {
                    AppData.Log($"board: unreadable view: {error.Message}");
                }
                break;
            case "readFile" or "readDiff":
            {
                // Copy what the reply needs: the message is disposed when this handler returns.
                var root = message.RootElement;
                int request = root.TryGetProperty("request", out var r) && r.TryGetInt32(out int id) ? id : 0;
                string? path = root.TryGetProperty("path", out var p) ? p.GetString() : null;
                string? commit = root.TryGetProperty("commit", out var c) ? c.GetString() : null;
                if (typeProperty.GetString() == "readFile") _ = ReadFileAsync(request, path);
                else _ = ReadDiffAsync(request, path, commit);
                break;
            }
            case "outline" or "lineHistory":
            {
                var root = message.RootElement;
                int request = root.TryGetProperty("request", out var r) && r.TryGetInt32(out int id) ? id : 0;
                string? path = root.TryGetProperty("path", out var p) ? p.GetString() : null;
                if (typeProperty.GetString() == "outline") _ = ReadOutlineAsync(request, path);
                else _ = ReadLineHistoryAsync(request, path);
                break;
            }
            case "mentions":
            {
                var root = message.RootElement;
                int request = root.TryGetProperty("request", out var r) && r.TryGetInt32(out int id) ? id : 0;
                string? name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                string? path = root.TryGetProperty("path", out var p) ? p.GetString() : null;
                _ = FindMentionsAsync(request, name, path);
                break;
            }
            case "relations":
            {
                // What changes with a selected file, from the scan on screen; quick enough to answer at once.
                var root = message.RootElement;
                int request = root.TryGetProperty("request", out var r) && r.TryGetInt32(out int id) ? id : 0;
                string? path = root.TryGetProperty("path", out var p) ? p.GetString() : null;
                var relations = index is { } shown && shown.Get(path) is { IsFile: true } file ? ChangesWith.For(shown, file) : null;
                Post(new { type = "relations", request, relations });
                break;
            }
        }
    }

    void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Open a project folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            InitialDirectory = projectPath is not null ? Path.GetDirectoryName(projectPath) ?? "" : "",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) StartScan(dialog.SelectedPath);
    }

    /// <summary>
    /// Scans a project. A quiet scan is an automatic refresh: no progress display, and if a scan
    /// is already running it is queued behind it instead of cancelling it.
    /// </summary>
    async void StartScan(string path, bool quiet = false)
    {
        if (quiet && scanning)
        {
            rescanPending = true;
            return;
        }
        scanCancellation?.Cancel();
        var cancellation = scanCancellation = new CancellationTokenSource();
        projectPath = path;
        scanning = true;
        rescanPending = false;
        if (quiet)
        {
            Post(new { type = "refreshing" });
        }
        else
        {
            Text = $"{Path.GetFileName(path.TrimEnd('\\', '/'))} — FloorPlan";
            Post(new { type = "scanning", path });
        }
        var progress = quiet ? null : new Progress<ScanProgress>(p =>
        {
            if (!cancellation.IsCancellationRequested) Post(new { type = "progress", phase = p.Phase, done = p.Done, total = p.Total });
        });
        var options = new ScanOptions { IncludeUntracked = !AppData.LoadSettings().For(path).HideUntracked };

        try
        {
            var scan = await Task.Run(() => scanner.ScanAsync(path, options, progress, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            var scanned = new ProjectIndex(scan);
            // Opening a project briefs the human on what landed since they last saw it.
            bool opening = scan.Project.Root != boardRoot;
            var since = opening ? await BriefingAsync(scanned, OpeningMark(scan)) : null;
            // The description is re-read and its anchors re-checked on every scan, so a broken one shows as stale.
            var describedAt = launch.Description ?? AppData.DescriptionPath(scan.Project.Root);
            var checkedDescription = File.Exists(describedAt) ? await Task.Run(() => Descriptions.Load(describedAt, scanned)) : null;
            if (cancellation.IsCancellationRequested) return;
            lastScan = scan;
            index = scanned;
            projectPath = scan.Project.Root;
            if (opening && launch.SmokeCapture is null) AppData.UpdateSettings(s => s.LastProject = scan.Project.Root);
            Text = $"{scan.Project.Name} — FloorPlan";
            ServeBoard(scan.Project.Root);
            if (opening) board.SinceLastSeen = since;
            description = checkedDescription;
            Post(new { type = "scan", scan, since, description });
            Watch(scan);
            WatchDescription(describedAt);
            if (quiet) AppData.Log($"refresh {scan.Project.DurationMs} ms · {scan.Tree.Files} files · {scan.Commits.Count} commits · {scan.Project.Changes} changes");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is DirectoryNotFoundException or GitException or IOException or UnauthorizedAccessException)
        {
            // A refresh can collide with a Git operation in progress; the next change retries it.
            if (quiet)
            {
                AppData.Log($"refresh failed: {e.Message}");
                Post(new { type = "refreshed" });
            }
            else
            {
                Post(new { type = "error", message = e.Message, path });
                if (launch.SmokeCapture is not null) _ = FinishSmokeAsync(failed: true);
            }
        }
        finally
        {
            if (scanCancellation == cancellation)
            {
                scanning = false;
                if (rescanPending && projectPath is not null) StartScan(projectPath, quiet: true);
            }
        }
    }

    sealed record FileText(string? Value, bool Binary, bool Truncated);

    /// <summary>Resolves a path from the front end; only files in the last scan are readable.</summary>
    bool TryResolve(string? path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? full)
    {
        full = null;
        return index is not null && index.TryGetFile(path, out full);
    }

    static FileText Decode(byte[] bytes)
    {
        if (LineCounter.LooksBinary(bytes)) return new FileText(null, true, false);
        bool truncated = bytes.Length > ReadLimit;
        var span = truncated ? bytes.AsSpan(0, ReadLimit) : bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) span = span[3..];
        return new FileText(System.Text.Encoding.UTF8.GetString(span), false, truncated);
    }

    async Task<FileText> ReadWorkingAsync(string full) =>
        File.Exists(full) ? Decode(await LineCounter.ReadAsync(full, ReadLimit + 1, CancellationToken.None)) : new FileText(null, false, false);

    /// <summary>A file's content at a revision, as the raw blob (no filters or text conversion).</summary>
    async Task<FileText> ReadBlobAsync(string revision, string path)
    {
        var result = await git.RunAsync(lastScan!.Project.Root, ["cat-file", "blob", $"{revision}:./{path}"], CancellationToken.None);
        return result.ExitCode == 0 ? Decode(result.Output) : new FileText(null, false, false);
    }

    async Task ReadFileAsync(int request, string? path)
    {
        try
        {
            if (!TryResolve(path, out var full)) { Post(new { type = "file", request, error = "Not a file in this project" }); return; }
            var text = await ReadWorkingAsync(full);
            Post(new { type = "file", request, text = text.Value, binary = text.Binary, truncated = text.Truncated });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Post(new { type = "file", request, error = e.Message });
        }
    }

    /// <summary>A file's outline for the reader's skeleton, parsed off the interface thread.</summary>
    async Task ReadOutlineAsync(int request, string? path)
    {
        try
        {
            if (!TryResolve(path, out var full)) { Post(new { type = "outline", request, error = "Not a file in this project" }); return; }
            var (outline, note) = await Outlines.ReadAsync(full, path!, CancellationToken.None);
            Post(new { type = "outline", request, outline, note });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Post(new { type = "outline", request, error = e.Message });
        }
    }

    /// <summary>The files whose text mentions a name, leaving out the one it is declared in.</summary>
    async Task FindMentionsAsync(int request, string? name, string? path)
    {
        try
        {
            if (name is null || lastScan is not { Project.Git: true } scan) { Post(new { type = "mentions", request, mentions = (MentionReport?)null }); return; }
            var except = index?.Get(path) is { IsFile: true } file ? file.Path : null;
            var mentions = await Mentions.FindAsync(git, scan.Project.Root, name, except, CancellationToken.None);
            Post(new { type = "mentions", request, mentions });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or GitUnavailableException)
        {
            Post(new { type = "mentions", request, error = e.Message });
        }
    }

    /// <summary>Which lines of a file each loaded commit changed, for the map inside the file.</summary>
    async Task ReadLineHistoryAsync(int request, string? path)
    {
        try
        {
            if (!TryResolve(path, out _) || lastScan is not { Project.Git: true } scan) { Post(new { type = "lineHistory", request, error = "No history for this file" }); return; }
            var history = await LineHistory.ForAsync(git, scan.Project.Root, path!, CancellationToken.None);
            Post(new { type = "lineHistory", request, history });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or GitUnavailableException)
        {
            Post(new { type = "lineHistory", request, error = e.Message });
        }
    }

    /// <summary>Before and after texts for a file: a commit against its parent, or the working file against HEAD.</summary>
    async Task ReadDiffAsync(int request, string? path, string? commit)
    {
        try
        {
            if (!TryResolve(path, out var full) || lastScan is not { Project.Git: true }) { Post(new { type = "diff", request, error = "No history for this file" }); return; }
            FileText before, after;
            if (commit == "working")
            {
                before = await ReadBlobAsync("HEAD", path!);
                after = await ReadWorkingAsync(full);
            }
            else if (commit is not null && System.Text.RegularExpressions.Regex.IsMatch(commit, "^[0-9a-f]{7,40}$"))
            {
                before = await ReadBlobAsync(commit + "^", path!);
                after = await ReadBlobAsync(commit, path!);
            }
            else
            {
                Post(new { type = "diff", request, error = "Unknown commit" });
                return;
            }
            Post(new
            {
                type = "diff",
                request,
                before = before.Value,
                after = after.Value,
                binary = before.Binary || after.Binary,
                truncated = before.Truncated || after.Truncated,
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or GitUnavailableException)
        {
            Post(new { type = "diff", request, error = e.Message });
        }
    }

    /// <summary>Watches the scanned project and refreshes it when something on the map changes.</summary>
    void Watch(ProjectScan scan)
    {
        var root = scan.Project.Root;
        if (watcher is not null && watchedRoot == root) return;
        watcher?.Dispose();
        watcher = null;
        watchedRoot = root;
        bool isGit = scan.Project.Git;
        var exclusions = new ScanOptions().FolderExclusions;
        try
        {
            watcher = new ProjectWatcher(root, scan.Project.GitDirectory, async (paths, lost) =>
            {
                bool relevant;
                try
                {
                    var known = index?.Paths ?? new HashSet<string>();
                    relevant = lost || await ChangeRelevance.AnyRelevantAsync(git, root, isGit, paths, known, exclusions, CancellationToken.None);
                }
                catch (Exception e) when (e is GitException or GitUnavailableException or IOException)
                {
                    relevant = true;
                }
                if (relevant && !IsDisposed)
                    BeginInvoke(() => { if (projectPath == root) StartScan(root, quiet: true); });
            });
        }
        catch (Exception e) when (e is ArgumentException or IOException or PlatformNotSupportedException)
        {
            AppData.Log($"watching {root} failed: {e.Message}");
        }
    }

    /// <summary>
    /// Watches the project's description file, which lives outside the project, so that an agent's
    /// describe or a hand edit shows at once, checked against the scan on screen.
    /// </summary>
    void WatchDescription(string file)
    {
        if (descriptionWatcher is not null && string.Equals(descriptionPath, file, StringComparison.OrdinalIgnoreCase)) return;
        descriptionWatcher?.Dispose();
        descriptionWatcher = null;
        descriptionPath = file;
        try
        {
            var folder = Path.GetDirectoryName(file)!;
            Directory.CreateDirectory(folder);
            var watching = new FileSystemWatcher(folder, Path.GetFileName(file)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            System.Threading.Timer? settle = null;
            void Changed(object? sender, FileSystemEventArgs e)
            {
                // Writers replace the file in steps; read it once they are done.
                settle?.Dispose();
                settle = new System.Threading.Timer(_ => { if (!IsDisposed) BeginInvoke(() => ReloadDescription(file)); }, null, 250, Timeout.Infinite);
            }
            watching.Changed += Changed;
            watching.Created += Changed;
            watching.Deleted += Changed;
            watching.Renamed += (sender, e) => Changed(sender, e);
            watching.EnableRaisingEvents = true;
            descriptionWatcher = watching;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            AppData.Log($"watching the description {file} failed: {e.Message}");
        }
    }

    void ReloadDescription(string file)
    {
        if (index is not { } shown || !string.Equals(descriptionPath, file, StringComparison.OrdinalIgnoreCase)) return;
        description = File.Exists(file) ? Descriptions.Load(file, shown) : null;
        Post(new { type = "description", description });
        AppData.Log($"description reloaded: {description?.Description?.Systems.Count ?? 0} systems, {description?.Stale?.Count ?? 0} stale");
    }

    void Post(object message)
    {
        if (web.CoreWebView2 is null || IsDisposed) return;
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, ScanJson.Options));
    }

    /// <summary>A different project starts a fresh board.</summary>
    void ServeBoard(string root)
    {
        if (boardRoot == root) return;
        board.Reset();
        boardRoot = root;
    }

    /// <summary>
    /// The human did something in the window. Back after a while, they are briefed on what landed
    /// meanwhile, and what the window shows becomes what they last saw.
    /// </summary>
    void Present()
    {
        var now = DateTimeOffset.Now;
        var away = now - board.LastActive;
        bool back = board.Touch(now);
        if (back) AppData.Log($"back after {away.TotalMinutes:F0} min");
        if (index is not { Scan.Project: { Git: true, Head: { } head } project } shown) return;
        // The mark is read before this presence replaces it.
        if (back) _ = BriefOnReturnAsync(shown, AppData.LoadSettings().Seen(project.Root));
        Saw(project.Root, head, now);
    }

    async Task BriefOnReturnAsync(ProjectIndex shown, SeenMark? mark)
    {
        var since = await BriefingAsync(shown, mark);
        if (since is null || index?.Root != shown.Root) return;
        board.SinceLastSeen = since;
        Post(new { type = "since", since });
    }

    /// <summary>What landed since the mark, stamped with when the window shows it; null when nothing did or Git failed.</summary>
    async Task<SinceReport?> BriefingAsync(ProjectIndex shown, SeenMark? mark)
    {
        try
        {
            var clock = Stopwatch.StartNew();
            var since = await Task.Run(() => Since.ComputeAsync(git, shown, mark, CancellationToken.None));
            if (since is null) return null;
            AppData.Log($"since {since.SeenHead} · {since.Commits} commits{(since.Problem is null ? "" : " · lost")} · {clock.ElapsedMilliseconds} ms");
            return since with { ShownAt = DateTimeOffset.Now };
        }
        catch (Exception e) when (e is GitException or GitUnavailableException or IOException)
        {
            AppData.Log($"since: {e.Message}");
            return null;
        }
    }

    /// <summary>Where the briefing on opening a project starts: the commit given at launch, else the HEAD stored as last seen.</summary>
    SeenMark? OpeningMark(ProjectScan scan)
    {
        if (seenAtLaunch is { } sha)
        {
            seenAtLaunch = null;
            long? time = scan.Commits.FirstOrDefault(c => c.Sha.Equals(sha, StringComparison.OrdinalIgnoreCase))?.Time;
            return new SeenMark(sha, time is { } t ? DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime() : DateTimeOffset.Now);
        }
        return AppData.LoadSettings().Seen(scan.Project.Root);
    }

    /// <summary>Stores the HEAD the human has seen: when it changes, and otherwise at most once a minute, for the time.</summary>
    void Saw(string root, string head, DateTimeOffset now)
    {
        // Test windows leave the settings alone.
        if (launch.SmokeCapture is not null) return;
        if (seenStored is { } stored && stored.Root == root && stored.Head == head && now - stored.At < TimeSpan.FromMinutes(1)) return;
        AppData.UpdateSettings(s =>
        {
            var project = s.For(root);
            project.SeenHead = head;
            project.SeenAt = now;
        });
        seenStored = (root, head, now);
    }

    ProjectIndex? IBoardSurface.Index => index;
    DescriptionReport? IBoardSurface.Description => description;
    BoardState IBoardSurface.State => board;
    bool IBoardSurface.Focused => ActiveForm == this;

    void IBoardSurface.Point(Pointer pointer, bool reveal) => Post(new
    {
        type = "pointer",
        agent = pointer.Agent,
        caption = pointer.Caption,
        items = pointer.Items,
        at = pointer.At.ToUnixTimeMilliseconds(),
        reveal,
    });

    void IBoardSurface.Raise() => NativeMethods.RaiseWithoutFocus(Handle);

    /// <summary>Runs work on the UI thread, which owns the scan, the board and the front end.</summary>
    Task<T> OnUiThread<T>(Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(() =>
            {
                try
                {
                    done.SetResult(work());
                }
                catch (Exception e)
                {
                    // Forwarded to the board server, which replies with the error.
                    done.SetException(e);
                }
            });
        }
        catch (InvalidOperationException)
        {
            done.SetException(new IOException("The FloorPlan window is closing"));
        }
        return done.Task;
    }

    void OnRendered(JsonElement message)
    {
        // Only the first map after launch is a startup measurement; refreshes render too.
        if (firstMapLogged) return;
        firstMapLogged = true;
        if (launch.Open is { } open)
        {
            int at = open.LastIndexOf('@');
            Post(new { type = "openReader", path = at > 0 ? open[..at] : open, commit = at > 0 ? open[(at + 1)..] : null });
        }
        var sinceStart = DateTime.Now - Process.GetCurrentProcess().StartTime;
        int items = message.TryGetProperty("items", out var i) ? i.GetInt32() : 0;
        AppData.Log($"first map {sinceStart.TotalMilliseconds:F0} ms after launch · scan {lastScan?.Project.DurationMs} ms · " +
                    $"{lastScan?.Tree.Files} files · {items} drawn · {lastScan?.Project.Root}");
        if (launch.SmokeCapture is not null && !smokeDone) _ = FinishSmokeAsync(failed: false, sinceStart);
    }

    /// <summary>Smoke check: wait for the window to settle, record measurements, capture it and exit.</summary>
    async Task FinishSmokeAsync(bool failed, TimeSpan? firstMap = null)
    {
        if (smokeDone) return;
        smokeDone = true;
        await Task.Delay(launch.Settle);

        long hostPrivate = Process.GetCurrentProcess().PrivateMemorySize64;
        long webPrivate = 0;
        int webProcesses = 0;
        foreach (var info in web.CoreWebView2.Environment.GetProcessInfos())
        {
            try
            {
                using var process = Process.GetProcessById(info.ProcessId);
                webPrivate += process.PrivateMemorySize64;
                webProcesses++;
            }
            catch (ArgumentException) { }
        }

        var capture = launch.SmokeCapture!;
        Directory.CreateDirectory(Path.GetDirectoryName(capture)!);
        await using (var file = File.Create(capture))
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file);

        var metrics = new
        {
            firstMapMs = firstMap?.TotalMilliseconds,
            scanMs = lastScan?.Project.DurationMs,
            files = lastScan?.Tree.Files,
            commits = lastScan?.Commits.Count,
            hostPrivateMiB = hostPrivate / 1048576.0,
            webViewPrivateMiB = webPrivate / 1048576.0,
            webViewProcesses = webProcesses,
            clientSize = $"{ClientSize.Width}x{ClientSize.Height}",
            dpi = DeviceDpi,
        };
        File.WriteAllText(Path.ChangeExtension(capture, ".json"), JsonSerializer.Serialize(metrics, ScanJson.Indented));
        ExitCode = failed ? 1 : 0;
        Close();
    }
}

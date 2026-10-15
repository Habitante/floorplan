using System.Diagnostics;
using System.Text.Json;
using FloorPlan.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace FloorPlan.App;

/// <summary>
/// The window: hosts the front end in WebView2, serves it from embedded resources and
/// answers a small set of messages (open, rescan). The front end has no filesystem access.
/// </summary>
sealed class MainForm : Form
{
    const string Origin = "https://floorplan.invalid/";
    static readonly string? DevUrl = Environment.GetEnvironmentVariable("FLOORPLAN_DEV_URL");

    readonly LaunchOptions launch;
    readonly WebView2 web;
    readonly WebAssets assets = new();
    readonly GitRunner git = new();
    readonly ProjectScanner scanner;
    readonly Settings settings = AppData.LoadSettings();
    CancellationTokenSource? scanCancellation;
    string? projectPath;
    ProjectScan? lastScan;
    bool scanning;
    bool rescanPending;
    ProjectWatcher? watcher;
    string? watchedRoot;
    IReadOnlySet<string> knownPaths = new HashSet<string>();
    IReadOnlySet<string> knownFiles = new HashSet<string>();
    const int ReadLimit = 1024 * 1024;
    bool smokeDone;
    bool firstMapLogged;

    public int ExitCode { get; private set; }

    public MainForm(LaunchOptions launch)
    {
        this.launch = launch;
        scanner = new ProjectScanner(git);
        projectPath = launch.Path is { } p ? Path.GetFullPath(p) : settings.LastProject;
        Text = "FloorPlan";
        bool dark = NativeMethods.AppsUseDarkTheme();
        BackColor = dark ? Color.FromArgb(0x0d, 0x0d, 0x0d) : Color.FromArgb(0xf9, 0xf9, 0xf7);
        StartPosition = FormStartPosition.CenterScreen;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        ClientSize = launch.Size ?? new Size((int)(area.Width * 0.8), (int)(area.Height * 0.85));
        MinimumSize = new Size(560, 420);
        web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        Controls.Add(web);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.UseDarkTitleBar(Handle, NativeMethods.AppsUseDarkTheme());
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
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
            _ = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => BeginInvoke(() =>
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
        base.OnFormClosed(e);
    }

    async Task InitializeWebViewAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, AppData.WebView);
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
        core.Navigate(DevUrl ?? Origin + "index.html");
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
                if (projectPath is not null && message.RootElement.TryGetProperty("untracked", out var untracked))
                {
                    settings.For(projectPath).IncludeUntracked = untracked.ValueKind == JsonValueKind.True;
                    AppData.Save(settings);
                    StartScan(projectPath);
                }
                break;
            case "rendered":
                OnRendered(message.RootElement);
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
        var options = new ScanOptions { IncludeUntracked = settings.For(path).IncludeUntracked };

        try
        {
            var scan = await Task.Run(() => scanner.ScanAsync(path, options, progress, cancellation.Token), cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            lastScan = scan;
            projectPath = scan.Project.Root;
            settings.LastProject = scan.Project.Root;
            AppData.Save(settings);
            Text = $"{scan.Project.Name} — FloorPlan";
            Post(new { type = "scan", scan });
            Watch(scan);
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
    bool TryResolve(string? path, out string full)
    {
        full = "";
        if (lastScan is null || path is null || !knownFiles.Contains(path)) return false;
        var root = lastScan.Project.Root;
        full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
        var known = new HashSet<string>(StringComparer.Ordinal);
        void Collect(FloorPlan.Core.TreeNode node)
        {
            if (node.Path.Length > 0) known.Add(node.Path);
            foreach (var child in node.Children ?? []) Collect(child);
        }
        Collect(scan.Tree);
        knownPaths = known;
        var files = new HashSet<string>(StringComparer.Ordinal);
        void CollectFiles(FloorPlan.Core.TreeNode node)
        {
            if (node.IsFile) files.Add(node.Path);
            foreach (var child in node.Children ?? []) CollectFiles(child);
        }
        CollectFiles(scan.Tree);
        knownFiles = files;

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
                    relevant = lost || await ChangeRelevance.AnyRelevantAsync(git, root, isGit, paths, knownPaths, exclusions, CancellationToken.None);
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

    void Post(object message)
    {
        if (web.CoreWebView2 is null || IsDisposed) return;
        web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, ScanJson.Options));
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
        await Task.Delay(TimeSpan.FromSeconds(3));

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

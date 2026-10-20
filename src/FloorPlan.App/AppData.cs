using System.Text.Json;
using System.Text.Json.Serialization;
using FloorPlan.Core;

namespace FloorPlan.App;

/// <summary>FloorPlan's own data folder. Nothing is ever written to an inspected project.</summary>
static class AppData
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FloorPlan");

    public static string WebView => Path.Combine(Folder, "WebView2");
    /// <summary>Smoke runs keep their own browser data, apart from any open window.</summary>
    public static string SmokeWebView => Path.Combine(Folder, "WebView2-smoke");
    public static string LogFile => Path.Combine(Folder, "floorplan.log");
    /// <summary>Where agents' project descriptions live by default, one per project folder name.</summary>
    public static string Descriptions => Path.Combine(Folder, "descriptions");

    /// <summary>
    /// Where a project's description belongs: the file its settings name, else the default place
    /// for its folder name. Never inside the project.
    /// </summary>
    public static string DescriptionPath(string root)
    {
        if (LoadSettings().For(root).Description is { Length: > 0 } named) return named;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
        return Path.Combine(Descriptions, $"{name}.floorplan.json");
    }

    /// <summary>The project's description file, or null when there is none yet.</summary>
    public static string? DescriptionFile(string root) => DescriptionPath(root) is var file && File.Exists(file) ? file : null;
    static string SettingsFile => Path.Combine(Folder, "settings.json");

    /// <summary>An optional licensed icon font the user placed in FloorPlan's data folder.</summary>
    public static string? IconFont()
    {
        foreach (var name in new[] { "icons.woff2", "icons.otf", "icons.ttf" })
        {
            var path = Path.Combine(Folder, "fonts", name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>
    /// Windows and agents' servers share the settings file, so it is read fresh for each use and
    /// changed under a lock that every FloorPlan process takes.
    /// </summary>
    const string SettingsLock = @"Local\FloorPlan.Settings";

    public static Settings LoadSettings()
    {
        using var held = Lock();
        return Read();
    }

    /// <summary>Re-reads the settings, applies a change and writes them back, so changes from other windows are kept.</summary>
    public static void UpdateSettings(Action<Settings> change)
    {
        using var held = Lock();
        var settings = Read();
        change(settings);
        try
        {
            Directory.CreateDirectory(Folder);
            var temporary = SettingsFile + ".tmp";
            var options = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, options));
            File.Move(temporary, SettingsFile, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static Settings Read()
    {
        try
        {
            return File.Exists(SettingsFile)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new Settings()
                : new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    /// <summary>Holds the settings lock until disposed; after a short wait, carries on without it.</summary>
    static IDisposable Lock()
    {
        var mutex = new Mutex(false, SettingsLock);
        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(2));
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }
        return new Held(mutex, owned);
    }

    sealed class Held(Mutex mutex, bool owned) : IDisposable
    {
        public void Dispose()
        {
            if (owned) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }

    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var log = new FileInfo(LogFile);
            if (log.Exists && log.Length > 1024 * 1024) File.Move(LogFile, LogFile + ".old", overwrite: true);
            File.AppendAllText(LogFile, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}

sealed class Settings
{
    public int Version { get; set; } = 1;
    public string? LastProject { get; set; }
    /// <summary>Per-project scan choices, keyed by lower-case project root.</summary>
    public Dictionary<string, ProjectSettings> Projects { get; set; } = [];

    public ProjectSettings For(string root)
    {
        var key = Key(root);
        if (!Projects.TryGetValue(key, out var project)) Projects[key] = project = new ProjectSettings();
        return project;
    }

    /// <summary>The HEAD the human last saw on a project, and when.</summary>
    public SeenMark? Seen(string root) =>
        Projects.GetValueOrDefault(Key(root)) is { SeenHead: { } head, SeenAt: { } at } ? new SeenMark(head, at) : null;

    static string Key(string root) => Path.GetFullPath(root).TrimEnd('\\', '/').ToLowerInvariant();
}

sealed class ProjectSettings
{
    /// <summary>
    /// New files that Git does not track yet are on the map unless the human hides them. Replaces an
    /// IncludeUntracked flag that defaulted to off; its stored values were defaults and are ignored.
    /// </summary>
    public bool HideUntracked { get; set; }
    /// <summary>HEAD when the human was last present in a window on the project: where "since I was last here" starts.</summary>
    public string? SeenHead { get; set; }
    public DateTimeOffset? SeenAt { get; set; }
    /// <summary>The project's description, written by an agent and kept outside the project.</summary>
    public string? Description { get; set; }
}

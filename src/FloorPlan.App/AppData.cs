using System.Text.Json;

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

    public static Settings LoadSettings()
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

    public static void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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
        var key = Path.GetFullPath(root).TrimEnd('\\', '/').ToLowerInvariant();
        if (!Projects.TryGetValue(key, out var project)) Projects[key] = project = new ProjectSettings();
        return project;
    }
}

sealed class ProjectSettings
{
    public bool IncludeUntracked { get; set; }
}

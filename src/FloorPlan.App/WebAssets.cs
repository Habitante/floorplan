using System.Reflection;

namespace FloorPlan.App;

/// <summary>The built front end, embedded in the executable as "web/..." resources.</summary>
sealed class WebAssets
{
    readonly Assembly assembly = typeof(WebAssets).Assembly;
    readonly Dictionary<string, string> resources;

    public WebAssets()
    {
        resources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("web/", StringComparison.Ordinal) || n.StartsWith("web\\", StringComparison.Ordinal))
            .ToDictionary(n => n[4..].Replace('\\', '/'), n => n, StringComparer.OrdinalIgnoreCase);
    }

    public Stream? Open(string path) =>
        resources.TryGetValue(path, out var name) ? assembly.GetManifestResourceStream(name) : null;

    public static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".otf" => "font/otf",
        ".ttf" => "font/ttf",
        _ => "application/octet-stream",
    };
}

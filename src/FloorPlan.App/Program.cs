namespace FloorPlan.App;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args is ["scan", ..]) return ScanCommand.Run(args[1..]);
        if (args is ["mcp", ..]) return McpCommand.Run(args[1..]);

        var launch = LaunchOptions.Parse(args);
        ApplicationConfiguration.Initialize();
        using var form = new MainForm(launch);
        Application.Run(form);
        return form.ExitCode;
    }
}

/// <summary>
/// <c>floorplan [folder] [--open file[@commit|@working]] [--theme light|dark] [--fill activity|age|kind|systems] [--seen sha] [--description file] [--smoke out.png] [--size WxH] [--settle seconds]</c>.
/// <c>--seen</c> takes a full commit hash as the HEAD last seen, instead of the stored one; <c>--description</c>
/// a project description to show instead of the one the settings name; <c>--fill</c> a lens to start in, for checks.
/// </summary>
sealed record LaunchOptions(string? Path, string? SmokeCapture, Size? Size, string? Open, TimeSpan Settle, string? Theme, string? Seen, string? Description = null, string? Fill = null)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? path = null, smoke = null, open = null, theme = null, seen = null, description = null, fill = null;
        Size? size = null;
        var settle = TimeSpan.FromSeconds(3);
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--smoke" when i + 1 < args.Length:
                    smoke = System.IO.Path.GetFullPath(args[++i]);
                    break;
                case "--theme" when i + 1 < args.Length:
                    theme = args[++i] is "light" or "dark" ? args[i] : null;
                    break;
                case "--settle" when i + 1 < args.Length:
                    if (double.TryParse(args[++i], System.Globalization.CultureInfo.InvariantCulture, out double seconds) && seconds is >= 0 and <= 600)
                        settle = TimeSpan.FromSeconds(seconds);
                    break;
                case "--open" when i + 1 < args.Length:
                    open = args[++i];
                    break;
                case "--seen" when i + 1 < args.Length:
                    seen = args[++i] is { Length: 40 } sha && sha.All(char.IsAsciiHexDigit) ? sha : null;
                    break;
                case "--fill" when i + 1 < args.Length:
                    fill = args[++i] is "activity" or "age" or "kind" or "systems" ? args[i] : null;
                    break;
                case "--description" when i + 1 < args.Length:
                    description = System.IO.Path.GetFullPath(args[++i]);
                    break;
                case "--size" when i + 1 < args.Length:
                    var parts = args[++i].Split('x');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) size = new Size(w, h);
                    break;
                default:
                    path ??= args[i];
                    break;
            }
        }
        return new LaunchOptions(path, smoke, size, open, settle, theme, seen, description, fill);
    }
}

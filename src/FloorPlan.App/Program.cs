namespace FloorPlan.App;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args is ["scan", ..]) return ScanCommand.Run(args[1..]);

        var launch = LaunchOptions.Parse(args);
        ApplicationConfiguration.Initialize();
        using var form = new MainForm(launch);
        Application.Run(form);
        return form.ExitCode;
    }
}

/// <summary><c>floorplan [folder] [--open file[@commit|@working]] [--smoke out.png] [--size WxH]</c></summary>
sealed record LaunchOptions(string? Path, string? SmokeCapture, Size? Size, string? Open)
{
    public static LaunchOptions Parse(string[] args)
    {
        string? path = null, smoke = null, open = null;
        Size? size = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--smoke" when i + 1 < args.Length:
                    smoke = System.IO.Path.GetFullPath(args[++i]);
                    break;
                case "--open" when i + 1 < args.Length:
                    open = args[++i];
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
        return new LaunchOptions(path, smoke, size, open);
    }
}

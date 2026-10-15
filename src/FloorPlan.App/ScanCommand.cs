using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FloorPlan.Core;

namespace FloorPlan.App;

/// <summary><c>floorplan scan &lt;folder&gt; [--pretty] [--commits N]</c>: the scan model as JSON on stdout.</summary>
static class ScanCommand
{
    public static int Run(string[] args)
    {
        NativeMethods.AttachParentConsoleIfNeeded();
        string? path = null;
        bool pretty = false;
        var options = new ScanOptions();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--pretty") pretty = true;
            else if (args[i] == "--commits" && i + 1 < args.Length && int.TryParse(args[++i], out int n) && n > 0) options = options with { CommitLimit = n };
            else path ??= args[i];
        }
        if (path is null)
        {
            Error("Usage: floorplan scan <folder> [--pretty] [--commits N]");
            return 2;
        }

        try
        {
            var clock = Stopwatch.StartNew();
            var scan = new ProjectScanner(new GitRunner()).ScanAsync(path, options).GetAwaiter().GetResult();
            using (var stdout = Console.OpenStandardOutput())
            {
                JsonSerializer.Serialize(stdout, scan, pretty ? ScanJson.Indented : ScanJson.Options);
                stdout.WriteByte((byte)'\n');
            }
            Error($"{scan.Tree.Files:N0} files · {scan.Commits.Count:N0} commits · {clock.ElapsedMilliseconds:N0} ms");
            foreach (var issue in scan.Issues) Error($"  {issue.Path}: {issue.Message}");
            return 0;
        }
        catch (Exception e) when (e is DirectoryNotFoundException or GitException or IOException or UnauthorizedAccessException)
        {
            Error(e.Message);
            return 1;
        }
    }

    static void Error(string message)
    {
        using var writer = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
        writer.WriteLine(message);
    }
}

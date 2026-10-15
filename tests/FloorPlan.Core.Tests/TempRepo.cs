using System.Diagnostics;
using System.Text;

namespace FloorPlan.Core.Tests;

/// <summary>A throwaway folder, optionally a Git repository, deleted on dispose.</summary>
sealed class TempRepo : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "floorplan-tests", Guid.NewGuid().ToString("N")[..12]);

    public TempRepo(bool git = true)
    {
        Directory.CreateDirectory(Root);
        if (git) Git("init", "-q", "-b", "main");
    }

    public TempRepo Write(string path, string content)
    {
        var full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return this;
    }

    public TempRepo WriteBytes(string path, byte[] content)
    {
        var full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
        return this;
    }

    public TempRepo Commit(string message)
    {
        Git("add", "-A");
        Git("-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "-q", "--allow-empty", "-m", message);
        return this;
    }

    public string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.autocrlf=false");
        foreach (var a in arguments) start.ArgumentList.Add(a);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', arguments)}: {error}");
        return output;
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

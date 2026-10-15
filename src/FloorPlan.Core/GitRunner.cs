using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace FloorPlan.Core;

public sealed class GitUnavailableException(string message, Exception inner) : Exception(message, inner);

public sealed class GitException(string message) : Exception(message);

public sealed record GitResult(int ExitCode, byte[] Output, string Error)
{
    public string Text => Encoding.UTF8.GetString(Output).TrimEnd('\n', '\r');
}

/// <summary>
/// Runs the installed Git executable for read-only queries. Arguments are passed as a
/// literal array (no shell), and options that could write to the repository or run
/// repository-configured programs are switched off.
/// </summary>
public sealed class GitRunner(string executable = "git")
{
    static readonly string[] SafetyArguments =
    [
        "--no-pager",
        "--no-optional-locks",
        "-c", "core.fsmonitor=false",
        "-c", "core.quotePath=false",
        "-c", "color.ui=false",
        "-c", "log.showSignature=false",
        "-c", "i18n.logOutputEncoding=UTF-8",
    ];

    /// <summary>
    /// Put first in the arguments of commands that take paths after "--", so names containing
    /// glob characters or pathspec magic are matched literally. Not every command accepts it.
    /// </summary>
    public const string LiteralPathspecs = "--literal-pathspecs";

    public async Task<GitResult> RunAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in SafetyArguments) startInfo.ArgumentList.Add(argument);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception e)
        {
            throw new GitUnavailableException("Git executable not found", e);
        }

        process.StandardInput.Close();
        using var output = new MemoryStream();
        try
        {
            var copy = process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(copy, error);
            await process.WaitForExitAsync(cancellationToken);
            return new GitResult(process.ExitCode, output.ToArray(), error.Result.Trim());
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
    }

    public async Task<GitResult> RunCheckedAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var result = await RunAsync(workingDirectory, arguments, cancellationToken);
        if (result.ExitCode != 0)
            throw new GitException($"git {arguments[0]} failed ({result.ExitCode}): {result.Error}");
        return result;
    }
}

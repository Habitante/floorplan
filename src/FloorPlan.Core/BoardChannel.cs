using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FloorPlan.Core;

/// <summary>A request from <c>floorplan mcp</c> to the window showing its project.</summary>
public sealed class BoardRequest
{
    public int V { get; init; } = BoardChannel.Version;
    /// <summary>"selection", "show" or "summary".</summary>
    public string Op { get; init; } = "";
    public string? Agent { get; init; }
    public string? Caption { get; init; }
    public IReadOnlyList<string>? Paths { get; init; }
    public string? Path { get; init; }
    /// <summary>The agent's working folder, for relative paths.</summary>
    public string? Base { get; init; }
}

public sealed class BoardReply
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public JsonElement? Result { get; init; }

    public static BoardReply Success(object result) => new() { Ok = true, Result = JsonSerializer.SerializeToElement(result, ScanJson.Options) };
    public static BoardReply Failure(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// The named pipe between a window and <c>floorplan mcp</c>: one JSON line each way per
/// connection. Each window serves the pipe named after its project root, for the current user
/// only, and only the first window on a project serves it. A client looks for a window on its
/// working folder, then on each folder above it.
/// </summary>
public static class BoardChannel
{
    public const int Version = 1;
    const string Prefix = "floorplan-board-";
    const int MaxReplyBytes = 8 * 1024 * 1024;
    static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(10);

    public static string PipeName(string root)
    {
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToLowerInvariant();
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)).AsSpan(0, 8));
    }

    /// <summary>Pipe names for the folder and every folder above it, nearest first.</summary>
    public static IEnumerable<string> Candidates(string folder)
    {
        for (var dir = new DirectoryInfo(Path.GetFullPath(folder)); dir is not null; dir = dir.Parent)
            yield return PipeName(dir.FullName);
    }

    /// <summary>
    /// Sends a request to the window showing the folder or a folder above it. Null when no
    /// window is showing any of them.
    /// </summary>
    public static async Task<BoardReply?> RequestAsync(string folder, BoardRequest request, CancellationToken cancellationToken = default)
    {
        var existing = ExistingPipes();
        foreach (var name in Candidates(folder))
        {
            // Without a listing, probe briefly; a listed pipe may be between connections.
            if (existing is not null && !existing.Contains(name)) continue;
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await using (client)
            {
                try
                {
                    await client.ConnectAsync(existing is null ? 50 : 2000, cancellationToken);
                }
                catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ExchangeTimeout);
                await WriteLineAsync(client, JsonSerializer.SerializeToUtf8Bytes(request, ScanJson.Options), timeout.Token);
                var line = await ReadLineAsync(client, MaxReplyBytes, timeout.Token)
                    ?? throw new IOException("The FloorPlan window closed the connection without replying");
                return JsonSerializer.Deserialize<BoardReply>(line, ScanJson.Options)
                    ?? throw new IOException("The FloorPlan window sent an empty reply");
            }
        }
        return null;
    }

    /// <summary>Names of the named pipes that exist now, or null where they cannot be listed.</summary>
    static HashSet<string>? ExistingPipes()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return Directory.GetFiles(@"\\.\pipe\").Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal static async Task WriteLineAsync(Stream stream, byte[] json, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(json, cancellationToken);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>Reads up to a newline; null when the stream ends first.</summary>
    internal static async Task<byte[]?> ReadLineAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        var line = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0) return null;
            int end = Array.IndexOf(buffer, (byte)'\n', 0, read);
            line.Write(buffer, 0, end < 0 ? read : end);
            if (line.Length > limit) throw new IOException("Message too long");
            if (end >= 0) return line.ToArray();
        }
    }
}

/// <summary>
/// A window's end of the board channel. Takes the pipe for its project if no other window has
/// it, and keeps trying while another does, so the board follows whichever window stays open.
/// Requests are handled one connection at a time by <paramref name="handler"/>.
/// </summary>
public sealed class BoardServer : IAsyncDisposable
{
    static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(10);
    const int MaxRequestBytes = 256 * 1024;

    readonly string name;
    readonly Func<BoardRequest, Task<BoardReply>> handler;
    readonly Action<string> log;
    readonly TimeSpan retry;
    readonly CancellationTokenSource stop = new();
    readonly Task loop;

    public BoardServer(string root, Func<BoardRequest, Task<BoardReply>> handler, Action<string> log)
        : this(root, handler, log, TimeSpan.FromSeconds(5)) { }

    internal BoardServer(string root, Func<BoardRequest, Task<BoardReply>> handler, Action<string> log, TimeSpan retry)
    {
        name = BoardChannel.PipeName(root);
        this.handler = handler;
        this.log = log;
        this.retry = retry;
        loop = Task.Run(ListenAsync);
    }

    public string PipeName => name;

    /// <summary>True while this window serves the project's pipe.</summary>
    public bool Serving { get; private set; }

    NamedPipeServerStream Create(bool first) => new(
        name,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None));

    async Task ListenAsync()
    {
        var token = stop.Token;
        NamedPipeServerStream? listener = null;
        bool waitingLogged = false;
        int failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                listener ??= Create(first: !Serving);
                Serving = true;
                await listener.WaitForConnectionAsync(token);
                var connected = listener;
                // Listen again before serving, so the pipe never disappears between clients.
                listener = Create(first: false);
                failures = 0;
                _ = ServeAsync(connected, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                listener?.Dispose();
                listener = null;
                TimeSpan pause;
                if (!Serving)
                {
                    // Another window serves this project.
                    if (!waitingLogged) log($"board: {name} is served by another window; retrying");
                    waitingLogged = true;
                    pause = retry;
                }
                else
                {
                    // A client that left mid-connection costs nothing; repeated failures back off.
                    if (++failures == 3) log($"board: {name}: {e.Message}");
                    pause = failures > 1 ? TimeSpan.FromMilliseconds(Math.Min(2000, 50 * failures)) : TimeSpan.Zero;
                }
                try { await Task.Delay(pause, token); } catch (OperationCanceledException) { break; }
            }
        }
        listener?.Dispose();
        Serving = false;
    }

    async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        await using (pipe)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ExchangeTimeout);
            try
            {
                var line = await BoardChannel.ReadLineAsync(pipe, MaxRequestBytes, timeout.Token);
                if (line is null) return;
                BoardReply reply;
                try
                {
                    var request = JsonSerializer.Deserialize<BoardRequest>(line, ScanJson.Options);
                    reply = request is null ? BoardReply.Failure("Empty request")
                        : request.V != BoardChannel.Version ? BoardReply.Failure($"This FloorPlan window speaks board version {BoardChannel.Version}, not {request.V}; restart one of them")
                        : await handler(request);
                }
                catch (JsonException e)
                {
                    reply = BoardReply.Failure($"Malformed request: {e.Message}");
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // The reply is the only place the agent can learn what went wrong.
                    log($"board: {e.GetType().Name}: {e.Message}");
                    reply = BoardReply.Failure(e.Message);
                }
                await BoardChannel.WriteLineAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(reply, ScanJson.Options), timeout.Token);
                // Keep the pipe open until the client has read the reply and hung up.
                var rest = new byte[256];
                while (await pipe.ReadAsync(rest, timeout.Token) > 0) { }
            }
            catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The client went away or took too long; it reports that itself.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { await loop; } catch (OperationCanceledException) { }
        stop.Dispose();
    }
}

using System.Globalization;
using System.IO.Pipes;
using System.Text.Json;

namespace FloorPlan.Core;

/// <summary>A request from <c>floorplan mcp</c> to a window.</summary>
public sealed class BoardRequest
{
    public int V { get; init; } = BoardChannel.Version;
    /// <summary>"hello", "selection", "show", "summary" or "raise".</summary>
    public string Op { get; init; } = "";
    public string? Agent { get; init; }
    public string? Caption { get; init; }
    public IReadOnlyList<string>? Paths { get; init; }
    public string? Path { get; init; }
    /// <summary>Bring what <c>show</c> points at into view: raise the window, zoom the map, open the reader.</summary>
    public bool Reveal { get; init; }
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

/// <summary>A window's answer to "hello".</summary>
/// <param name="Project">The project root on screen; null before a project is open.</param>
/// <param name="IdleSeconds">Since the human last did something in the window.</param>
public sealed record HelloReport(string? Project, string? Name, long IdleSeconds, bool Focused);

/// <summary>A window found on the board channel.</summary>
/// <param name="Note">Why the window cannot take part, such as another version.</param>
public sealed record BoardWindow(string Pipe, string? Project, string? Name, long IdleSeconds, bool Focused, string? Note = null);

/// <summary>
/// The named pipes between windows and <c>floorplan mcp</c>: one JSON line each way per call.
/// Every window serves its own pipe, for the current user only, and says which project it shows;
/// an agent's server finds the windows by listing the pipes, so one server serves every project.
/// </summary>
public static class BoardChannel
{
    public const int Version = 2;
    const string Prefix = "floorplan-board-";
    const int MaxReplyBytes = 8 * 1024 * 1024;
    static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(10);

    public static string PipeName(int processId) => Prefix + processId.ToString(CultureInfo.InvariantCulture);

    /// <summary>The open windows, the one the human used last first.</summary>
    public static async Task<IReadOnlyList<BoardWindow>> FindWindowsAsync(CancellationToken cancellationToken = default)
    {
        var found = await Task.WhenAll(ListPipes().Select(async pipe =>
        {
            BoardReply? reply;
            try
            {
                reply = await RequestAsync(pipe, new BoardRequest { Op = "hello" }, cancellationToken, connectMilliseconds: 500);
            }
            catch (Exception e) when ((e is IOException or JsonException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            if (reply is null) return null;
            if (!reply.Ok || reply.Result is not { } result) return new BoardWindow(pipe, null, null, long.MaxValue, false, reply.Error);
            var hello = result.Deserialize<HelloReport>(ScanJson.Options);
            return hello is null ? null : new BoardWindow(pipe, hello.Project, hello.Name, hello.IdleSeconds, hello.Focused);
        }));
        return found.OfType<BoardWindow>()
            .OrderByDescending(w => w.Focused)
            .ThenBy(w => w.IdleSeconds)
            .ThenBy(w => w.Pipe, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Sends a request to one window. Null when it cannot be reached.</summary>
    public static async Task<BoardReply?> RequestAsync(string pipe, BoardRequest request, CancellationToken cancellationToken = default, int connectMilliseconds = 2000)
    {
        var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using (client)
        {
            try
            {
                await client.ConnectAsync(connectMilliseconds, cancellationToken);
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
            {
                return null;
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

    /// <summary>Board pipes that exist now. Listing pipes needs Windows, like the window itself.</summary>
    static IReadOnlyList<string> ListPipes()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try
        {
            return Directory.GetFiles(@"\\.\pipe\")
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(n => n.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
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

/// <summary>A window's end of the board channel: its own pipe, answered by <paramref name="handler"/>.</summary>
public sealed class BoardServer : IAsyncDisposable
{
    static readonly TimeSpan ExchangeTimeout = TimeSpan.FromSeconds(10);
    const int MaxRequestBytes = 256 * 1024;

    readonly Func<BoardRequest, Task<BoardReply>> handler;
    readonly Action<string> log;
    readonly CancellationTokenSource stop = new();
    readonly Task loop;

    public BoardServer(string pipeName, Func<BoardRequest, Task<BoardReply>> handler, Action<string> log)
    {
        PipeName = pipeName;
        this.handler = handler;
        this.log = log;
        loop = Task.Run(ListenAsync);
    }

    public string PipeName { get; }

    /// <summary>True once the pipe exists.</summary>
    public bool Serving { get; private set; }

    NamedPipeServerStream Create() => new(
        PipeName,
        PipeDirection.InOut,
        NamedPipeServerStream.MaxAllowedServerInstances,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    async Task ListenAsync()
    {
        var token = stop.Token;
        NamedPipeServerStream? listener = null;
        int failures = 0;
        while (!token.IsCancellationRequested)
        {
            try
            {
                listener ??= Create();
                Serving = true;
                await listener.WaitForConnectionAsync(token);
                var connected = listener;
                // Listen again before serving, so the pipe never disappears between clients.
                listener = Create();
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
                // A client that left mid-connection costs nothing; repeated failures back off.
                if (++failures == 3) log($"board: {PipeName}: {e.Message}");
                var pause = failures > 1 ? TimeSpan.FromMilliseconds(Math.Min(2000, 50 * failures)) : TimeSpan.Zero;
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
                        : request.V != BoardChannel.Version ? BoardReply.Failure($"This FloorPlan window speaks board version {BoardChannel.Version}, not {request.V}; restart the older of the window and the agent's server")
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

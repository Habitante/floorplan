namespace FloorPlan.Core;

/// <summary>Counts physical lines: every line including blanks and comments.</summary>
public static class LineCounter
{
    /// <summary>Git's heuristic: a NUL byte in the first 8000 bytes means binary.</summary>
    public const int BinaryProbeBytes = 8000;

    public static bool LooksBinary(ReadOnlySpan<byte> data) =>
        data[..Math.Min(data.Length, BinaryProbeBytes)].IndexOf((byte)0) >= 0;

    /// <summary>Returns the physical line count, or null when the content looks binary.</summary>
    public static int? Count(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return 0;
        if (LooksBinary(data)) return null;
        int lines = data.Count((byte)'\n');
        if (data[^1] != (byte)'\n') lines++;
        return lines;
    }

    /// <summary>Opens a file without blocking concurrent writers (agents may be editing it).</summary>
    public static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    public static async Task<byte[]> ReadAsync(string path, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = OpenShared(path);
        var buffer = new byte[Math.Min(maxBytes, (int)Math.Min(stream.Length, int.MaxValue))];
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (n == 0) break;
            read += n;
        }
        return read == buffer.Length ? buffer : buffer[..read];
    }
}

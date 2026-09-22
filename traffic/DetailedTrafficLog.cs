using System.Text;
using System.Text.Json;

namespace ReVerse.Traffic;

public sealed class DetailedTrafficLog(string directoryPath, Func<bool>? enabledProvider = null) : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly object gate = new();
    private StreamWriter? writer;
    private string? currentPath;
    private bool enabled;
    private Exception? lastError;

    public string DirectoryPath { get; } = Path.GetFullPath(directoryPath);
    public bool Enabled { get => Volatile.Read(ref enabled); set => Volatile.Write(ref enabled, value); }
    public bool IsEnabled => Enabled || enabledProvider?.Invoke() == true;
    public Exception? LastError => Volatile.Read(ref lastError);
    public event Action<Exception>? Failed;

    public void Write(string type, object data)
    {
        if (!IsEnabled) return;
        var timestamp = DateTimeOffset.UtcNow;
        var path = Path.Combine(DirectoryPath, $"detailed-traffic-{timestamp:yyyy-MM-dd}.jsonl");
        var line = JsonSerializer.Serialize(new { timestampUtc = timestamp, type, data });
        Exception? failure = null;
        lock (gate)
        {
            try
            {
                if (currentPath != path)
                {
                    writer?.Dispose();
                    Directory.CreateDirectory(DirectoryPath);
                    writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 65536),
                        new UTF8Encoding(false)) { AutoFlush = true };
                    currentPath = path;
                }
                writer!.WriteLine(line);
                Volatile.Write(ref lastError, null);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failure = exception;
                try { writer?.Dispose(); } catch (IOException) { }
                writer = null;
                currentPath = null;
            }
        }
        if (failure is not null && Interlocked.Exchange(ref lastError, failure) is null)
            Failed?.Invoke(failure);
    }

    public static TrafficPayload Payload(ReadOnlySpan<byte> bytes)
    {
        string? text;
        try { text = Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { text = null; }
        return new TrafficPayload(bytes.Length, Convert.ToBase64String(bytes), text);
    }

    public void Dispose()
    {
        lock (gate)
        {
            writer?.Dispose();
            writer = null;
            currentPath = null;
        }
    }
}

public sealed record TrafficPayload(int ByteCount, string Base64, string? Utf8);

public sealed class TrafficTapStream(Stream inner, Action<ReadOnlyMemory<byte>>? onRead = null,
    Action<ReadOnlyMemory<byte>>? onWrite = null) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        if (read > 0) onRead?.Invoke(buffer.AsMemory(offset, read).ToArray());
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        if (read > 0) onRead?.Invoke(buffer[..read].ToArray());
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var read = await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
        if (read > 0) onRead?.Invoke(buffer.AsMemory(offset, read).ToArray());
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        if (read > 0) onRead?.Invoke(buffer[..read].ToArray());
        return read;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        inner.Write(buffer, offset, count);
        if (count > 0) onWrite?.Invoke(buffer.AsMemory(offset, count).ToArray());
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        inner.Write(buffer);
        if (!buffer.IsEmpty) onWrite?.Invoke(buffer.ToArray());
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        await inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
        if (count > 0) onWrite?.Invoke(buffer.AsMemory(offset, count).ToArray());
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteAsync(buffer, cancellationToken);
        if (!buffer.IsEmpty) onWrite?.Invoke(buffer.ToArray());
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
}

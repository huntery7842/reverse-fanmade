using System.Text;
using System.Text.Json;

namespace ReVerse.Capture.Capturing;

public sealed class RequestLog(string directoryPath) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public string DirectoryPath { get; } = directoryPath;

    public async Task WriteAsync(object value)
    {
        var line = JsonSerializer.Serialize(value, JsonOptions) + "\n";
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, $"requests-{DateTime.UtcNow:yyyy-MM-dd}.jsonl");
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                4096, FileOptions.Asynchronous);
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line));
            await stream.FlushAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
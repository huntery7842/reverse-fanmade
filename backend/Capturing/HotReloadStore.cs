using System.Text.Json;

namespace ReVerse.Capture.Capturing;





public abstract class HotReloadStore<TEntry> : IDisposable where TEntry : class
{
    private readonly string _filePath;
    private readonly FileSystemWatcher _watcher;
    private readonly object _lock = new();
    private Dictionary<string, TEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    protected HotReloadStore(string filePath)
    {
        _filePath = filePath;
        var directory = Path.GetDirectoryName(filePath)!;
        var fileName = Path.GetFileName(filePath);
        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        try
        {

            Thread.Sleep(100);
            Load();
        }
        catch (Exception exception)
        {

            Console.Error.WriteLine($"[{GetType().Name}] Reload failed: {exception.Message}");
        }
    }

    protected abstract Dictionary<string, TEntry>? Deserialize(string json);

    public void Load()
    {
        if (!File.Exists(_filePath))
        {
            lock (_lock) { _entries = new Dictionary<string, TEntry>(StringComparer.OrdinalIgnoreCase); }
            return;
        }

        var parsed = Deserialize(File.ReadAllText(_filePath));
        if (parsed is not null)
        {
            lock (_lock) { _entries = parsed; }
        }
    }

    public TEntry? GetEntry(string? path)
    {
        lock (_lock)
        {
            if (path is not null && _entries.TryGetValue(path, out var entry))
                return entry;
        }
        return null;
    }

    public void Dispose() => _watcher.Dispose();
}

public sealed class ResponseOverrideEntry
{
    public int StatusCode { get; set; } = 200;
    public string? Body { get; set; }
    public string? ContentType { get; set; }
}

public sealed class ContractEntry
{
    public string? Body { get; set; }
}


public sealed class ResponseOverrides : HotReloadStore<ResponseOverrideEntry>
{
    public ResponseOverrides(string filePath) : base(filePath) { }
    protected override Dictionary<string, ResponseOverrideEntry>? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, Serialization.SourceGenerationContext.Default.DictionaryStringResponseOverrideEntry);
}


public sealed class ContractOverrides : HotReloadStore<ContractEntry>
{
    public ContractOverrides(string filePath) : base(filePath) { }
    protected override Dictionary<string, ContractEntry>? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, Serialization.SourceGenerationContext.Default.DictionaryStringContractEntry);
}
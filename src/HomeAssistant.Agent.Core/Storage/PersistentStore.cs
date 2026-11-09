using System.Text.Json;

namespace HomeAssistant.Agent.Core.Storage;

// The JSON file behind the plugins' persistent state (PluginStorage scopes it
// per plugin). One instance per host process: the desktop app and the Windows
// service each have their own file, because both rewrite it whole on save.
//
// Formerly the static PluginPersistentStorage in Plugin.Base; it moved here so
// plugins only see the IPluginStorage contract, not the file behind it.
public sealed class PersistentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, JsonElement> _data;

    private PersistentStore(string filePath, Dictionary<string, JsonElement> data)
    {
        FilePath = filePath;
        _data = data;
    }

    public string FilePath { get; }

    // Reads the file once; a missing or empty file is an empty store. Small
    // enough to load synchronously while the host is being built.
    public static PersistentStore Load(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath is empty", nameof(filePath));

        var data = new Dictionary<string, JsonElement>();

        if (File.Exists(filePath))
        {
            var json = File.ReadAllText(filePath);

            if (!string.IsNullOrWhiteSpace(json))
                data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions) ?? data;
        }

        return new PersistentStore(filePath, data);
    }

    public async Task<T?> GetAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (!_data.TryGetValue(key, out var element))
                return defaultValue;

            try
            {
                return element.Deserialize<T>(JsonOptions);
            }
            catch (JsonException)
            {
                return defaultValue;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            _data[key] = JsonSerializer.SerializeToElement(value, JsonOptions);
            await SaveCoreAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (!_data.Remove(key))
                return false;

            await SaveCoreAsync(cancellationToken);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    // Moves a value to a new key unless the new key is already set. For key
    // format changes between versions; returns whether anything moved.
    public async Task<bool> RenameAsync(string oldKey, string newKey, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (!_data.Remove(oldKey, out var value))
                return false;

            _data.TryAdd(newKey, value);
            await SaveCoreAsync(cancellationToken);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    // Written next to the target and swapped in, so a crash mid-write never
    // leaves a truncated file (and lost tokens) behind. Caller holds the lock.
    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = FilePath + ".tmp";

        await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(_data, JsonOptions), cancellationToken);

        if (File.Exists(FilePath))
            File.Replace(tempPath, FilePath, null);
        else
            File.Move(tempPath, FilePath);
    }
}

using System.Text.Json;

namespace HomeAssistant.Agent.Core.Updates;

// plugins.lock.json in the data directory: which plugins came from which
// catalog. Written by the elevated plugin step when it installs one; the
// updater reads it to know where to look for that plugin's updates. Plugins
// with no entry were bundled with the agent (or copied in by hand) and are
// looked up in the official catalog.
public sealed class PluginLock
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public sealed record Entry(string Version, string Source, string Sha256, string FileName);

    public PluginLock(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public static string DefaultPath => Path.Combine(AgentPaths.DataDirectory, "plugins.lock.json");

    // Plugin code -> entry; empty when the file is missing or unreadable.
    public Dictionary<string, Entry> Read()
    {
        if (!File.Exists(FilePath))
            return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(FilePath), JsonOptions);
            return new Dictionary<string, Entry>(entries ?? [], StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Write(Dictionary<string, Entry> entries)
    {
        var directory = Path.GetDirectoryName(FilePath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(entries, JsonOptions));

        if (File.Exists(FilePath))
            File.Replace(tempPath, FilePath, null);
        else
            File.Move(tempPath, FilePath);
    }
}

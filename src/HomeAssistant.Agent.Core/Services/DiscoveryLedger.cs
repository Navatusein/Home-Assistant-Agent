using System.Text.Json;

namespace HomeAssistant.Agent.Core.Services;

// What each host last published to Home Assistant: the discovery topics of
// its previous run, one file per MQTT device (Discovery.<device>.json). The
// next run deletes exactly the topics it no longer publishes.
//
// Per device, not one file: the desktop app and the Windows service share the
// data directory but publish different entity sets under different MQTT
// devices — one shared record would make each delete the other's discovery.
public sealed class DiscoveryLedger(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Directory { get; } = directory;

    public string GetPath(string mqttDevice) => Path.Combine(Directory, $"Discovery.{mqttDevice}.json");

    // An unreadable record counts as empty: better to leave a stale entity
    // behind than to delete discovery that may still be in use.
    public HashSet<string> Read(string mqttDevice)
    {
        var path = GetPath(mqttDevice);

        if (!File.Exists(path))
            return new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var topics = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path));
            return new HashSet<string>(topics ?? [], StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    public void Write(string mqttDevice, IEnumerable<string> topics)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(GetPath(mqttDevice), JsonSerializer.Serialize(topics.Order(StringComparer.Ordinal).ToArray(), JsonOptions));
    }
}

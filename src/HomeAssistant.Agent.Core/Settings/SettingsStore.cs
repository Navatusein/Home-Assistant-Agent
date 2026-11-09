using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeAssistant.Agent.Core.Settings;

// Reads and writes Settings.json (see AgentSettings for its shape).
public sealed class SettingsStore(string filePath)
{
    // Relaxed escaping keeps the file readable by hand: Cyrillic device names,
    // quoted paths and "Alt+F10" stay as typed instead of \u escapes.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string FilePath { get; } = filePath;

    // A missing file is empty settings — the first start.
    public AgentSettings Load()
    {
        return File.Exists(FilePath) ? Parse(File.ReadAllText(FilePath)) : new AgentSettings();
    }

    // Written next to the target and swapped in: the service reads the file as
    // soon as it changes and must never see it half-written. The temporary
    // name does not match the service's "Settings.json" watch filter.
    public void Save(AgentSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, Serialize(settings));

        if (File.Exists(FilePath))
            File.Replace(tempPath, FilePath, null);
        else
            File.Move(tempPath, FilePath);
    }

    public static string Serialize(AgentSettings settings)
    {
        var root = new JsonObject();

        foreach (var (name, values) in settings.Sections)
            root[name] = JsonSerializer.SerializeToNode(values);

        root[AgentSettings.PluginsSection] = JsonSerializer.SerializeToNode(settings.Plugins);

        return root.ToJsonString(JsonOptions);
    }

    // Also reads the flat format from before "plugins" existed, so the first
    // save after an update rewrites the file in the new shape.
    public static AgentSettings Parse(string json)
    {
        var settings = new AgentSettings();

        if (JsonNode.Parse(json) is not JsonObject root)
            return settings;

        foreach (var (name, node) in root)
        {
            if (name == AgentSettings.PluginsSection)
            {
                if (node is not JsonObject plugins)
                    continue;

                foreach (var (code, entry) in plugins)
                    settings.Plugins[code] = ParsePlugin(entry);
            }
            else if (name.StartsWith(PluginSettings.LegacySectionPrefix, StringComparison.Ordinal))
            {
                var code = name[PluginSettings.LegacySectionPrefix.Length..];

                // A plugin already in the new shape wins over a leftover.
                if (!settings.Plugins.ContainsKey(code))
                    settings.Plugins[code] = PluginSettings.FromLegacy(ParseSection(node));
            }
            else
                settings.Sections[name] = ParseSection(node);
        }

        return settings;
    }

    // One broken entry costs that plugin its settings, not the whole file.
    private static PluginSettings ParsePlugin(JsonNode? node)
    {
        try
        {
            var settings = node?.Deserialize<PluginSettings>() ?? new PluginSettings();

            // A "null" in the file must not leave the collections missing.
            settings.Features ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            settings.Config ??= new JsonObject();

            return settings;
        }
        catch (JsonException)
        {
            return new PluginSettings();
        }
    }

    private static Dictionary<string, string> ParseSection(JsonNode? node)
    {
        try
        {
            return node?.Deserialize<Dictionary<string, string>>() ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}

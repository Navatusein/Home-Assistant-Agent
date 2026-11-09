using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Plugin.Base;
using Serilog.Events;

namespace HomeAssistant.Agent.Core.Settings;

// One plugin's entry under "plugins" in Settings.json. What the agent decides
// about the plugin (on/off, features, log level) is kept apart from the
// plugin's own settings form ("config"), whose keys are the plugin's business.
public sealed class PluginSettings
{
    // Whether the user has the plugin switched on.
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    // The plugin's own log level; null follows the general level.
    [JsonPropertyName("logLevel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogLevel { get; set; }

    // Feature code -> switched on. A missing feature takes its own default.
    [JsonPropertyName("features")]
    public Dictionary<string, bool> Features { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // The values of the plugin's settings form, by control key, as the form
    // produces them: text as a string, a number field as a number, a switch as
    // true/false, a list as an array of objects. Handed to the plugin as is
    // (ConfigValues).
    [JsonPropertyName("config")]
    public JsonObject Config { get; set; } = new();

    public bool IsFeatureEnabled(PluginFeature feature) =>
        Features.TryGetValue(feature.Code, out var enabled) ? enabled : feature.EnabledByDefault;

    public LogEventLevel? GetLogLevel() => LogLevels.TryParse(LogLevel, out var level) ? level : null;

    public PluginSettings Clone() => new()
    {
        Enabled = Enabled,
        LogLevel = LogLevel,
        Features = new Dictionary<string, bool>(Features, StringComparer.OrdinalIgnoreCase),
        Config = (JsonObject) Config.DeepClone(),
    };

    // The format before 2026-10: a flat "plugin:<code>" section of strings,
    // where the agent's own keys were told apart from the form's by a "$"
    // prefix and a list was its JSON inside a string.
    internal const string LegacySectionPrefix = "plugin:";

    internal static PluginSettings FromLegacy(IReadOnlyDictionary<string, string> values)
    {
        const string featurePrefix = "$Feature:";

        var settings = new PluginSettings();

        foreach (var (key, value) in values)
        {
            if (key == "$Enabled")
                settings.Enabled = !bool.TryParse(value, out var enabled) || enabled;
            else if (key == "$LogLevel")
                settings.LogLevel = value;
            else if (key.StartsWith(featurePrefix, StringComparison.Ordinal))
            {
                if (bool.TryParse(value, out var featureEnabled))
                    settings.Features[key[featurePrefix.Length..]] = featureEnabled;
            }
            else
                settings.Config[key] = FromLegacyValue(value);
        }

        return settings;
    }

    // Which keys were lists is not recorded; only a list stored a JSON array,
    // so that is what one looks like. Everything else stays text: the form
    // turns a number field or a switch into its type on the next save.
    private static JsonNode? FromLegacyValue(string value)
    {
        if (value.TrimStart().StartsWith('['))
        {
            try
            {
                if (JsonNode.Parse(value) is JsonArray array)
                    return array;
            }
            catch (JsonException)
            {
            }
        }

        return JsonValue.Create(value);
    }
}

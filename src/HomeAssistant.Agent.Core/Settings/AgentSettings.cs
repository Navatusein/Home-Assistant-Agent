namespace HomeAssistant.Agent.Core.Settings;

// Settings.json in memory:
//
//   {
//     "general": { "DeviceName": "...", ... },
//     "plugins": {
//       "discord-plugin": {
//         "enabled": true,
//         "logLevel": "Debug",
//         "features": { "voice-controls": true },
//         "config": { "ClientId": "...", ... }
//       }
//     }
//   }
//
// The application's own sections are flat key -> value; every plugin has a
// structured entry under "plugins". The desktop app writes it; the Windows
// service watches it and re-reads it on every change.
public sealed class AgentSettings
{
    public const string PluginsSection = "plugins";

    // Application sections by name ("general"), including any this build no
    // longer knows: they survive a save untouched.
    public Dictionary<string, Dictionary<string, string>> Sections { get; init; } = new();

    // By plugin code, including plugins that are not installed right now, so
    // their settings come back when they are.
    public Dictionary<string, PluginSettings> Plugins { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> GetSection(string name) =>
        Sections.TryGetValue(name, out var values) ? values : new Dictionary<string, string>();

    // A plugin without an entry yet has the defaults.
    public PluginSettings GetPlugin(string pluginCode) =>
        Plugins.TryGetValue(pluginCode, out var settings) ? settings : new PluginSettings();

    public AgentSettings Clone() => new()
    {
        Sections = Sections.ToDictionary(pair => pair.Key, pair => new Dictionary<string, string>(pair.Value)),
        Plugins = Plugins.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.OrdinalIgnoreCase),
    };
}

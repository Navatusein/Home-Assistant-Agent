using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Tools;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.ViewModels.Settings.Controls;

// Fetches select options from a plugin's tools, once per tool rather than once
// per control: a list of three audio devices holds three selects pointing at
// the same tool, and enumerating the endpoints for each of them would be both
// slow and pointless.
public sealed class PluginToolOptionsProvider(BasePlugin plugin, ILogger logger)
{
    private readonly Dictionary<string, Task<IReadOnlyList<PluginToolOption>>> _cache =
        new(StringComparer.Ordinal);

    public Task<IReadOnlyList<PluginToolOption>> GetAsync(string toolName)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(toolName, out var cached))
                return cached;

            var task = LoadAsync(toolName);
            _cache[toolName] = task;

            return task;
        }
    }

    // Drops the cache so the next request asks the plugin again — devices come
    // and go while the settings page is open.
    public void Invalidate()
    {
        lock (_cache)
            _cache.Clear();
    }

    private async Task<IReadOnlyList<PluginToolOption>> LoadAsync(string toolName)
    {
        try
        {
            return PluginToolOptions.Deserialize(await plugin.InvokeToolAsync(toolName));
        }
        catch (Exception ex)
        {
            // A missing or throwing tool is a plugin problem; the form still
            // has to open, with an empty list and the stored value intact.
            logger.LogError(ex, "Plugin {PluginCode} failed to list options via tool '{Tool}'",
                plugin.PluginCode, toolName);

            return [];
        }
    }
}
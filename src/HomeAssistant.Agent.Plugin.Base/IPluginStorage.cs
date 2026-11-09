namespace HomeAssistant.Agent.Plugin.Base;

// Persistent key-value state of one plugin, stored as JSON by the host. Keys
// are the plugin's own ("auth-token"): the host scopes them to the plugin, so
// two plugins can use the same key without seeing each other's value.
//
// Each host keeps its own store — the desktop app and the Windows service run
// as different users and do not share plugin state.
public interface IPluginStorage
{
    // The stored value, or defaultValue when the key is missing or no longer
    // deserializes to T.
    Task<T?> GetAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
}

using HomeAssistant.Agent.Plugin.Base;

namespace HomeAssistant.Agent.Core.Storage;

// One plugin's view of the shared store: every key is stored as
// "<plugin code>:<key>", so plugins cannot read or overwrite each other's
// state, and the file stays readable by eye.
public sealed class PluginStorage(PersistentStore store, string pluginCode) : IPluginStorage
{
    public const char Separator = ':';

    public static string GetStoreKey(string pluginCode, string key) => pluginCode + Separator + key;

    public Task<T?> GetAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default) =>
        store.GetAsync(GetStoreKey(pluginCode, key), defaultValue, cancellationToken);

    public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken = default) =>
        store.SetAsync(GetStoreKey(pluginCode, key), value, cancellationToken);

    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default) =>
        store.RemoveAsync(GetStoreKey(pluginCode, key), cancellationToken);
}

using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Core.Storage;

// Keys written before plugin storage was scoped per plugin. Each plugin used
// to build its own full key; scoped storage now builds "<code>:<key>".
//
// Teams already used exactly that shape ("teams-plugin:api-token"), so its
// pairing carries over untouched — which matters, because Teams only issues a
// new token after the user confirms a pairing prompt again.
public static class StorageMigrations
{
    private static readonly (string OldKey, string NewKey)[] RenamedKeys =
    [
        ("discord-plugin.auth-token", PluginStorage.GetStoreKey("discord-plugin", "auth-token")),
    ];

    public static async Task ApplyAsync(PersistentStore store, ILogger logger)
    {
        foreach (var (oldKey, newKey) in RenamedKeys)
        {
            if (await store.RenameAsync(oldKey, newKey))
                logger.LogInformation("Moved stored {OldKey} to {NewKey}", oldKey, newKey);
        }
    }
}

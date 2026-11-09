using HomeAssistant.Agent.Core.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeAssistant.Agent.Tests;

public class StorageTests
{
    [Fact]
    public async Task Plugins_do_not_see_each_others_keys()
    {
        using var temp = new TempDirectory();
        var store = PersistentStore.Load(temp.File("store.json"));

        var discord = new PluginStorage(store, "discord-plugin");
        var teams = new PluginStorage(store, "teams-plugin");

        await discord.SetAsync("token", "d");
        await teams.SetAsync("token", "t");

        Assert.Equal("d", await discord.GetAsync<string>("token"));
        Assert.Equal("t", await teams.GetAsync<string>("token"));
        Assert.True(await teams.RemoveAsync("token"));
        Assert.Equal("d", await discord.GetAsync<string>("token"));
    }

    [Fact]
    public async Task Survives_a_reload()
    {
        using var temp = new TempDirectory();

        await new PluginStorage(PersistentStore.Load(temp.File("store.json")), "p").SetAsync("n", 42);

        Assert.Equal(42, await new PluginStorage(PersistentStore.Load(temp.File("store.json")), "p").GetAsync<int>("n"));
    }

    // The Discord token moves to the scoped key; the Teams pairing token was
    // already stored under it and must stay where it is — losing it means
    // pairing again in Teams.
    [Fact]
    public async Task Migration_moves_the_old_discord_key_and_leaves_teams_alone()
    {
        using var temp = new TempDirectory();
        var path = temp.File("store.json");

        File.WriteAllText(path, """
            {
              "discord-plugin.auth-token": { "Token": "D" },
              "teams-plugin:api-token": "T"
            }
            """);

        var store = PersistentStore.Load(path);

        await StorageMigrations.ApplyAsync(store, NullLogger.Instance);
        await StorageMigrations.ApplyAsync(store, NullLogger.Instance);

        var discord = await new PluginStorage(store, "discord-plugin").GetAsync<Dictionary<string, string>>("auth-token");

        Assert.Equal("D", discord!["Token"]);
        Assert.Equal("T", await new PluginStorage(store, "teams-plugin").GetAsync<string>("api-token"));
        Assert.DoesNotContain("discord-plugin.auth-token", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_value_of_the_wrong_shape_reads_as_the_default()
    {
        using var temp = new TempDirectory();
        var storage = new PluginStorage(PersistentStore.Load(temp.File("store.json")), "p");

        await storage.SetAsync("n", "not a number");

        Assert.Equal(7, await storage.GetAsync("n", 7));
    }
}

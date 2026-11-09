using System.Text.Json;
using System.Text.Json.Nodes;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Core.Settings;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Connector.Entities;
using Serilog.Events;

namespace HomeAssistant.Agent.Tests;

public class GeneralSettingsTests
{
    [Fact]
    public void Missing_values_fall_back_to_defaults()
    {
        var settings = GeneralSettings.FromValues(null);

        Assert.False(settings.AutoStart);
        Assert.Null(settings.DeviceName);
        Assert.Null(settings.MqttBroker);
        Assert.Equal(1883, settings.MqttPort);
        Assert.Equal("homeassistant-agent", settings.RootTopic);
        Assert.Equal(LogEventLevel.Information, settings.LogLevel);
        Assert.False(settings.CanConnect);
    }

    [Fact]
    public void Round_trips_through_the_stored_values()
    {
        var settings = new GeneralSettings
        {
            AutoStart = true,
            DeviceName = "My PC",
            MqttBroker = "broker.lan",
            MqttPort = 8883,
            MqttUser = "agent",
            MqttPassword = "secret",
            RootTopic = "custom",
            LogLevel = LogEventLevel.Debug,
            CheckForUpdates = false,
            PluginSources = ["https://github.com/a/b/releases/download/index/index.json"],
        };

        var restored = GeneralSettings.FromValues(settings.ToValues());

        Assert.Equal(settings.ToValues(), restored.ToValues());
        Assert.Equal(settings.PluginSources, restored.PluginSources);
    }

    // Settings saved before updates existed check for updates.
    [Fact]
    public void Update_checks_default_to_on()
    {
        Assert.True(GeneralSettings.FromValues(new Dictionary<string, string>()).CheckForUpdates);
        Assert.Empty(GeneralSettings.FromValues(new Dictionary<string, string> { ["PluginSources"] = "not json" }).PluginSources);
    }

    // The values the files already on disk contain must keep meaning the same.
    [Fact]
    public void Reads_the_existing_file_format()
    {
        var settings = GeneralSettings.FromValues(new Dictionary<string, string>
        {
            ["AutoStart"] = "True",
            ["DeviceName"] = " My PC ",
            ["MqttBroker"] = "broker.lan",
            ["MqttPort"] = "1884",
            ["MqttUser"] = "",
            ["RootTopic"] = "",
            ["LogLevel"] = "Information",
        });

        Assert.True(settings.AutoStart);
        Assert.Equal("My PC", settings.DeviceName);
        Assert.Equal(1884, settings.MqttPort);
        Assert.Null(settings.MqttUser);
        Assert.Equal("homeassistant-agent", settings.RootTopic);
        Assert.Equal(LogEventLevel.Information, settings.LogLevel);
        Assert.True(settings.CanConnect);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("70000")]
    public void An_invalid_port_falls_back_to_the_default(string port)
    {
        var settings = GeneralSettings.FromValues(new Dictionary<string, string> { ["MqttPort"] = port });

        Assert.Equal(1883, settings.MqttPort);
    }
}

public class PluginSettingsTests
{
    private static readonly EntityFeature OnByDefault =
        new("on", "On", "", PluginHostMode.Desktop, Array.Empty<IEntity>());

    private static readonly EntityFeature OffByDefault =
        new("off", "Off", "", PluginHostMode.Desktop, () => [], enabledByDefault: false);

    [Fact]
    public void A_plugin_without_an_entry_is_on()
    {
        Assert.True(new AgentSettings().GetPlugin("x").Enabled);
    }

    [Fact]
    public void Feature_switch_defaults_to_the_feature_default()
    {
        var empty = new PluginSettings();

        Assert.True(empty.IsFeatureEnabled(OnByDefault));
        Assert.False(empty.IsFeatureEnabled(OffByDefault));

        var settings = new PluginSettings { Features = { ["on"] = false, ["off"] = true } };

        Assert.False(settings.IsFeatureEnabled(OnByDefault));
        Assert.True(settings.IsFeatureEnabled(OffByDefault));
    }

    [Fact]
    public void Plugin_log_level_is_optional()
    {
        Assert.Null(new PluginSettings().GetLogLevel());
        Assert.Equal(LogEventLevel.Debug, new PluginSettings { LogLevel = "Debug" }.GetLogLevel());
    }

    [Theory]
    [InlineData("Trace", LogEventLevel.Verbose)]
    [InlineData("verbose", LogEventLevel.Verbose)]
    [InlineData("Info", LogEventLevel.Information)]
    [InlineData("Warning", LogEventLevel.Warning)]
    public void Log_level_names_parse(string name, LogEventLevel expected)
    {
        Assert.True(LogLevels.TryParse(name, out var level));
        Assert.Equal(expected, level);
    }
}

public class SettingsStoreTests
{
    [Fact]
    public void A_missing_file_is_empty_settings()
    {
        using var temp = new TempDirectory();

        var settings = new SettingsStore(temp.File("Settings.json")).Load();

        Assert.Empty(settings.Sections);
        Assert.Empty(settings.Plugins);
    }

    [Fact]
    public void Saves_atomically_and_reads_back()
    {
        using var temp = new TempDirectory();
        var store = new SettingsStore(temp.File("Settings.json"));

        store.Save(new AgentSettings { Sections = { ["general"] = new() { ["DeviceName"] = "first" } } });
        store.Save(new AgentSettings { Sections = { ["general"] = new() { ["DeviceName"] = "second" } } });

        Assert.Equal("second", store.Load().GetSection("general")["DeviceName"]);
        Assert.False(File.Exists(store.FilePath + ".tmp"));
    }

    // The agent's decisions and the plugin's own form stay apart in the file.
    [Fact]
    public void Plugins_are_nested()
    {
        var json = SettingsStore.Serialize(new AgentSettings
        {
            Sections = { ["general"] = new() { ["DeviceName"] = "pc" } },
            Plugins =
            {
                ["obs-plugin"] = new PluginSettings
                {
                    Enabled = false,
                    Features = { ["scenes"] = false },
                    Config = { ["Host"] = "localhost" },
                },
            },
        });

        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var plugin = root["plugins"]!["obs-plugin"]!;

        Assert.False(plugin["enabled"]!.GetValue<bool>());
        Assert.False(plugin["features"]!["scenes"]!.GetValue<bool>());
        Assert.Equal("localhost", plugin["config"]!["Host"]!.GetValue<string>());
        Assert.Null(plugin["logLevel"]);

        var restored = SettingsStore.Parse(json).GetPlugin("obs-plugin");

        Assert.False(restored.Enabled);
        Assert.Equal("localhost", new ConfigValues(restored.Config).GetString("Host"));
    }

    // The form's values keep their types in the file — a list is an array,
    // not a string holding JSON — and reach the plugin the same way.
    [Fact]
    public void Config_values_keep_their_types()
    {
        var settings = new AgentSettings
        {
            Plugins =
            {
                ["windows-plugin"] = new PluginSettings
                {
                    Config =
                    {
                        ["Hotkeys"] = new JsonArray(new JsonObject { ["Name"] = "Микрофон", ["Keys"] = "Alt+F10" }),
                        ["ClipboardSlots"] = 4,
                        ["Enabled"] = true,
                    },
                },
            },
        };

        var json = SettingsStore.Serialize(settings);
        var config = JsonNode.Parse(json)!["plugins"]!["windows-plugin"]!["config"]!;

        Assert.Equal(JsonValueKind.Array, config["Hotkeys"]!.GetValueKind());
        Assert.Equal(JsonValueKind.Number, config["ClipboardSlots"]!.GetValueKind());
        Assert.Equal(JsonValueKind.True, config["Enabled"]!.GetValueKind());
        Assert.Contains("Микрофон", json);
        Assert.Contains("Alt+F10", json);

        var values = new ConfigValues(SettingsStore.Parse(json).GetPlugin("windows-plugin").Config);

        Assert.Equal("Alt+F10", Assert.Single(values.GetList<Dictionary<string, string>>("Hotkeys")!)["Keys"]);
        Assert.Equal(4, values.GetInt("ClipboardSlots", 0));
        Assert.True(values.GetBool("Enabled", false));
    }

    // Settings.json from before the nesting: flat "plugin:<code>" sections
    // with the agent's keys marked by "$".
    [Fact]
    public void Reads_the_flat_format()
    {
        var settings = SettingsStore.Parse("""
            {
              "general": { "DeviceName": "pc" },
              "plugin:obs-plugin": {
                "Host": "localhost",
                "Hotkeys": "[{\"Name\":\"A\"}]",
                "$Feature:scenes": "False",
                "$Feature:broken": "garbage",
                "$Enabled": "False",
                "$LogLevel": "Debug"
              },
              "plugin:teams-plugin": { "$Enabled": "garbage" }
            }
            """);

        var obs = settings.GetPlugin("obs-plugin");

        Assert.Equal("pc", settings.GetSection("general")["DeviceName"]);
        Assert.False(obs.Enabled);
        Assert.Equal("Debug", obs.LogLevel);
        Assert.Equal(new Dictionary<string, bool> { ["scenes"] = false }, obs.Features);
        Assert.Equal("localhost", obs.Config["Host"]!.GetValue<string>());
        Assert.Equal("A", obs.Config["Hotkeys"]![0]!["Name"]!.GetValue<string>());
        Assert.True(settings.GetPlugin("teams-plugin").Enabled);
        Assert.DoesNotContain(settings.Sections.Keys, name => name.StartsWith("plugin:"));
    }

    // One broken plugin entry must not take the rest of the file with it.
    [Fact]
    public void A_broken_plugin_entry_falls_back_to_defaults()
    {
        var settings = SettingsStore.Parse("""
            {
              "general": { "DeviceName": "pc" },
              "plugins": {
                "obs-plugin": { "enabled": "maybe" },
                "teams-plugin": { "enabled": false }
              }
            }
            """);

        Assert.True(settings.GetPlugin("obs-plugin").Enabled);
        Assert.False(settings.GetPlugin("teams-plugin").Enabled);
        Assert.Equal("pc", settings.GetSection("general")["DeviceName"]);
    }
}

public class DiscoveryLedgerTests
{
    [Fact]
    public void Records_per_device()
    {
        using var temp = new TempDirectory();
        var ledger = new DiscoveryLedger(temp.Path);

        ledger.Write("pc", ["b", "a"]);
        ledger.Write("pc-svc", ["c"]);

        Assert.Equal(["a", "b"], ledger.Read("pc").Order());
        Assert.Equal(["c"], ledger.Read("pc-svc"));
        Assert.Empty(ledger.Read("other"));
    }

    // A broken record must not make the next run delete live discovery.
    [Fact]
    public void An_unreadable_record_counts_as_empty()
    {
        using var temp = new TempDirectory();
        var ledger = new DiscoveryLedger(temp.Path);

        File.WriteAllText(ledger.GetPath("pc"), "{ not json");

        Assert.Empty(ledger.Read("pc"));
    }
}

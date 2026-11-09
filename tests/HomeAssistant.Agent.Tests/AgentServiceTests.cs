using System.Net;
using System.Net.Sockets;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Core.Settings;
using HomeAssistant.Agent.Core.Storage;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Connector.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet.Server;

namespace HomeAssistant.Agent.Tests;

// The whole apply path against an in-process broker: settings in, plugins and
// features switched, discovery published and cleaned up, recorded in the ledger.
public sealed class AgentServiceTests : IAsyncLifetime
{
    private readonly TempDirectory _temp = new();
    private readonly int _port = FreePort();
    private MqttServer _server = null!;
    private BasePlugin? _plugin;

    public async Task InitializeAsync()
    {
        _server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(_port).Build());

        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        _server.Dispose();
        _temp.Dispose();
    }

    [Fact]
    public async Task Publishes_the_enabled_features_of_the_host()
    {
        var (agent, ledger) = CreateAgent(PluginHostMode.Desktop);

        Assert.Empty(await Apply(agent));

        Assert.Equal(["test_app_a_switch", "test_app_b_sensor"], UniqueIds(ledger, "test"));
    }

    [Fact]
    public async Task A_switched_off_feature_is_removed()
    {
        var (agent, ledger) = CreateAgent(PluginHostMode.Desktop);

        await Apply(agent);
        await Apply(agent, new PluginSettings { Features = { ["app-b"] = false } });

        Assert.Equal(["test_app_a_switch"], UniqueIds(ledger, "test"));
    }

    // A switched-off plugin stays in Home Assistant as unavailable, so its
    // entities keep their place.
    [Fact]
    public async Task A_switched_off_plugin_keeps_its_entities()
    {
        var (agent, ledger) = CreateAgent(PluginHostMode.Desktop);

        await Apply(agent, new PluginSettings { Enabled = false });

        Assert.Equal(2, UniqueIds(ledger, "test").Length);
        Assert.True(agent.IsConnected);
        Assert.False(_plugin!.IsEnabled);
    }

    [Fact]
    public async Task A_plugin_without_config_is_skipped()
    {
        var (agent, ledger) = CreateAgent(PluginHostMode.Desktop);

        Assert.Empty(await Apply(agent, new PluginSettings { Config = { [TestPlugin.UnconfiguredKey] = "yes" } }));

        Assert.Empty(UniqueIds(ledger, "test"));
    }

    [Fact]
    public async Task The_service_runs_only_service_features()
    {
        var (agent, ledger) = CreateAgent(PluginHostMode.Service);

        await Apply(agent);

        Assert.Equal(["test_svc_service_button"], UniqueIds(ledger, "test-svc"));
    }

    [Fact]
    public async Task Reports_connection_changes()
    {
        var (agent, _) = CreateAgent(PluginHostMode.Desktop);
        var events = new List<bool>();
        agent.ConnectionChanged += connected => { lock (events) events.Add(connected); };

        await Apply(agent);
        await agent.ShutdownAsync();

        Assert.Equal([true, false], events);
    }

    [Fact]
    public async Task Without_a_broker_it_reports_a_problem_instead_of_throwing()
    {
        var (agent, _) = CreateAgent(PluginHostMode.Desktop);

        agent.SaveSettings(new AgentSettings { Sections = { [GeneralSettings.Section] = new() } });

        var errors = await agent.ApplySettingsAsync();

        Assert.Single(errors);
        Assert.False(agent.IsConnected);
    }

    // The broker is down when the settings are applied (boot, a Home Assistant
    // restart) and comes up later: the agent connects and starts the plugins
    // without another apply.
    [Fact]
    public async Task Retries_a_broker_that_was_down_at_apply()
    {
        await _server.StopAsync();
        _server.Dispose();

        var (agent, ledger) = CreateAgent(PluginHostMode.Desktop);
        var connected = new TaskCompletionSource();
        agent.ConnectionChanged += up => { if (up) connected.TrySetResult(); };

        Assert.NotEmpty(await Apply(agent));
        Assert.False(agent.IsConnected);

        _server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(_port).Build());
        await _server.StartAsync();

        await connected.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // Connected is reported before the plugins start; the ledger is last.
        for (var i = 0; i < 50 && UniqueIds(ledger, "test").Length == 0; i++)
            await Task.Delay(100);

        Assert.True(agent.IsConnected);
        Assert.True(_plugin!.IsEnabled);
        Assert.Equal(["test_app_a_switch", "test_app_b_sensor"], UniqueIds(ledger, "test"));

        await agent.ShutdownAsync();
    }

    private (AgentService Agent, DiscoveryLedger Ledger) CreateAgent(PluginHostMode host)
    {
        var manager = new PluginManager(host, NullLoggerFactory.Instance,
            PersistentStore.Load(_temp.File($"store-{host}.json")), _temp.File("no-plugins"));

        _plugin = new TestPlugin();
        manager.Register(_plugin);

        var ledger = new DiscoveryLedger(_temp.Path);
        var agent = new AgentService(manager, settingsStore: new SettingsStore(_temp.File("Settings.json")), ledger: ledger);

        return (agent, ledger);
    }

    private async Task<IReadOnlyList<string>> Apply(AgentService agent, PluginSettings? plugin = null)
    {
        var general = new GeneralSettings { DeviceName = "Test", MqttBroker = "127.0.0.1", MqttPort = _port };

        agent.SaveSettings(new AgentSettings
        {
            Sections = { [GeneralSettings.Section] = general.ToValues() },
            Plugins = { [TestPlugin.Code] = plugin ?? new PluginSettings() },
        });

        return await agent.ApplySettingsAsync();
    }

    // "homeassistant/switch/test/test_app_a_switch/config" -> "test_app_a_switch".
    private static string[] UniqueIds(DiscoveryLedger ledger, string device) =>
        ledger.Read(device).Select(topic => topic.Split('/')[3]).Order().ToArray();

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // Two desktop features and one service feature, one entity each.
    private sealed class TestPlugin : BasePlugin
    {
        public const string Code = "test-plugin";
        public const string UnconfiguredKey = "Unconfigured";

        public TestPlugin()
        {
            AddFeature(new EntityFeature("app-a", "App A", "", PluginHostMode.Desktop,
                new SwitchEntity { Name = "A", UniqueId = "app_a_switch", BaseTopic = "test/a" }));

            AddFeature(new EntityFeature("app-b", "App B", "", PluginHostMode.Desktop,
                new SensorEntity { Name = "B", UniqueId = "app_b_sensor", BaseTopic = "test/b", DefaultState = "0" }));

            AddFeature(new EntityFeature("service", "Service", "", PluginHostMode.Service,
                new ButtonEntity { Name = "C", UniqueId = "service_button", BaseTopic = "test/c" }));
        }

        public override string PluginCode => Code;

        public override IPluginConfig? CreateConfig(ConfigValues values) =>
            values.Contains(UnconfiguredKey) ? null : new TestConfig();

        private sealed class TestConfig : IPluginConfig;
    }
}

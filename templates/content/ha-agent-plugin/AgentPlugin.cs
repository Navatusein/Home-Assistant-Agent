using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Connector.Entities;
using Microsoft.Extensions.Logging;

namespace MyPlugin;

// The agent finds the plugin by its BasePlugin base class and creates it with
// the parameterless constructor.
//
// A plugin is a set of features: groups of entities the user can switch on
// and off, each running either in the desktop app or in the Windows service.
// This one has a single feature with a switch that logs its state.
public sealed class AgentPlugin : BasePlugin
{
    // Stable: settings, logs and Home Assistant ids are keyed by it.
    public override string PluginCode => "my-plugin";

    private readonly SwitchEntity _exampleSwitch;

    private ILogger<AgentPlugin>? _logger;

    public AgentPlugin()
    {
        _exampleSwitch = new SwitchEntity
        {
            Name = "Example Switch",
            UniqueId = "my-plugin-example",
            BaseTopic = "my-plugin/example",
            Icon = "mdi:toggle-switch",
            ChangeStateHandler = ExampleSwitch_ChangeStateHandler,
        };

        AddFeature(new EntityFeature("example", "Example", "A switch that logs its state.",
            PluginHostMode.Desktop, _exampleSwitch));
    }

    // The settings form; the values reach CreateConfig by control key.
    public override ConfigDescriptor ConfigDescriptor { get; } = new(
        new InputTextControlDescriptor("Greeting", "Greeting", "Logged when the switch changes.",
            defaultValue: "Hello"));

    public override IPluginConfig CreateConfig(ConfigValues values) => new PluginConfig
    {
        Greeting = values.GetString("Greeting") ?? "Hello",
    };

    // The context (logger, storage, host mode) is available from here on.
    protected override void OnInitialized()
    {
        _logger = CreateLogger<AgentPlugin>();
    }

    // Answer with the state that actually took effect; it is what Home
    // Assistant shows.
    private Task<bool> ExampleSwitch_ChangeStateHandler(bool on)
    {
        var greeting = (Config as PluginConfig)?.Greeting;

        _logger?.LogInformation("{Greeting}: the switch is {State}", greeting, on ? "on" : "off");

        return Task.FromResult(on);
    }

    private sealed class PluginConfig : IPluginConfig
    {
        public required string Greeting { get; init; }
    }
}

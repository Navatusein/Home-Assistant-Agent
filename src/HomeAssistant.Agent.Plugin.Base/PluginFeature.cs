using HomeAssistant.Agent.Plugin.Base.Config.Controls;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Connector;
using HomeAssistant.Connector.Entities;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Base;

// One thing a plugin does, which the user can switch off on its own — "Volume",
// "Media", "Power". A feature owns a group of entities, says which host runs
// it (the desktop app or the Windows service), and may start and stop runtime
// state of its own (a monitor, a poller) and carry settings of its own.
//
// What all features of a plugin share — a connection to Discord or OBS, the
// typed config — stays with the plugin: its OnEnableAsync runs first, the
// enabled features' after it.
//
// Only enabled features of the current host are active: their entities are
// registered and their lifecycle runs. A switched-off feature registers
// nothing, so its entities disappear from Home Assistant.
public abstract class PluginFeature
{
    private BasePlugin? _plugin;

    protected PluginFeature(string code, string name, string description, PluginHostMode host,
        bool enabledByDefault = true)
    {
        Code = code;
        Name = name;
        Description = description;
        Host = host;
        EnabledByDefault = enabledByDefault;
        IsEnabled = enabledByDefault;
    }

    // Stable within the plugin: the on/off switch is stored under it.
    public string Code { get; }

    public string Name { get; }

    public string Description { get; }

    public PluginHostMode Host { get; }

    public bool EnabledByDefault { get; }

    // The user's switch. Changed by the host only while the plugin is
    // disabled; it takes effect with the next enable.
    public bool IsEnabled { get; internal set; }

    // Enabled, and meant for the host the plugin runs in.
    public bool IsActive => IsEnabled && _plugin != null && Host == _plugin.HostMode;

    // Settings shown under the feature's switch. Their values are stored with
    // the plugin's own and reach it through CreateConfig, like any other.
    public IReadOnlyList<BaseControl> Controls { get; init; } = [];

    protected BasePlugin Plugin =>
        _plugin ?? throw new InvalidOperationException($"Feature '{Code}' has not been added to a plugin.");

    // The client of the current enable cycle, for features that refresh their
    // own discovery (a select whose options change).
    protected HomeAssistantClient? Client => Plugin.CurrentClient;

    // The entities this feature exposes, built from the current config.
    public abstract IEntity[] GetEntities();

    internal void Attach(BasePlugin plugin)
    {
        if (_plugin != null)
            throw new InvalidOperationException($"Feature '{Code}' already belongs to a plugin.");

        _plugin = plugin;
    }

    // Runs on every SetConfigAsync of the plugin, for every feature (active or
    // not): entities that depend on the config are rebuilt here.
    protected internal virtual void OnConfigured(IPluginConfig config) { }

    protected internal virtual Task OnEnableAsync() => Task.CompletedTask;

    protected internal virtual Task OnDisableAsync() => Task.CompletedTask;

    protected ILogger<T> CreateLogger<T>() => Plugin.CreateFeatureLogger<T>();

    // See BasePlugin.FireAndForget.
    protected void FireAndForget(Task task, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(task))] string? operation = null)
    {
        Plugin.FireAndForgetFromFeature(task, operation);
    }
}

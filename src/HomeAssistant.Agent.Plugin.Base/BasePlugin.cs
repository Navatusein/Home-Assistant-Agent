using System.Runtime.CompilerServices;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Agent.Plugin.Base.Exceptions;
using HomeAssistant.Agent.Plugin.Base.Interfaces;
using HomeAssistant.Agent.Plugin.Base.Tools;
using HomeAssistant.Connector;
using HomeAssistant.Connector.Entities;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Base;

// A plugin is a set of features (PluginFeature) plus whatever they share: the
// typed config, a connection to an external app, the settings form. The host
// drives the lifecycle — Initialize, SetConfigAsync, EnableAsync/DisableAsync —
// and the plugin runs only the features that are both switched on and meant
// for the host it runs in.
public abstract class BasePlugin
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly List<PluginTool> _tools = [];
    private readonly List<PluginFeature> _features = [];

    // Entities registered with the client of the current enable cycle; they are
    // detached when it ends, so a late event cannot publish through a client
    // that is already gone.
    private IEntity[] _registeredEntities = [];

    private PluginContext? _context;
    private ILogger? _logger;

    // What the host hands the plugin right after constructing it: where it
    // runs, how it logs and where it keeps state. Available from Initialize
    // on — so not in the constructor, which runs before the host can call it.
    protected PluginContext Context =>
        _context ?? throw new PluginNotInitializedException($"Plugin '{PluginCode}' has not been initialized by the host yet.");

    // Which host runs the plugin: the desktop app or the Windows service.
    public PluginHostMode HostMode => Context.HostMode;

    // Key-value state that survives a restart, private to this plugin.
    protected IPluginStorage Storage => Context.Storage;

    // Plugins log only through this abstraction; the logging backend stays a
    // host concern.
    protected ILogger<T> CreateLogger<T>() => Context.LoggerFactory.CreateLogger<T>();

    private ILogger Logger => _logger ??= Context.LoggerFactory.CreateLogger(GetType());

    // Called once by the host, before any configuration or enable call.
    public void Initialize(PluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_context != null)
            throw new InvalidOperationException($"Plugin '{PluginCode}' is already initialized.");

        _context = context;
        OnInitialized();
    }

    // For setup that needs the context (a logger, the host mode); the
    // constructor runs too early for that.
    protected virtual void OnInitialized() { }

    // Stable identity: the settings section, log source and HA object ids are
    // keyed by it, so it stays in code rather than in editable metadata.
    public abstract string PluginCode { get; }

    // Name, description, author, links and icon from the project file.
    public PluginInfo Info => field ??= PluginInfo.FromAssembly(GetType().Assembly);

    // The display name comes from <PluginName> in the project file; plugins
    // without one fall back to their code.
    public virtual string PluginName => Info.Name ?? PluginCode;

    // The plugin-wide part of the settings form — what every feature needs,
    // like credentials or a host name. Settings of a single feature belong to
    // that feature (PluginFeature.Controls).
    public virtual ConfigDescriptor ConfigDescriptor { get; } = new();

    // Builds a typed config from the values collected by the settings form —
    // the plugin's and its features' controls alike. Returns null when the
    // values are insufficient to configure the plugin — the host then leaves
    // the plugin disabled.
    public virtual IPluginConfig? CreateConfig(ConfigValues values) => null;

    public bool IsEnabled { get; private set; }

    // Whether the current config is sufficient to run the plugin. EnableAsync
    // refuses to enable (before any entity is registered) when this is false;
    // plugins with mandatory settings override it.
    protected virtual bool CanEnable => true;

    protected IPluginConfig? Config { get; private set; }

    protected HomeAssistantClient? Client { get; private set; }

    internal HomeAssistantClient? CurrentClient => Client;

    public event EventHandler<bool>? EnabledChanged;

    public IReadOnlyList<PluginTool> Tools => _tools;

    public IReadOnlyList<PluginFeature> Features => _features;

    // Enabled features meant for this host — the ones that actually run.
    public IEnumerable<PluginFeature> ActiveFeatures => _features.Where(feature => feature.IsActive);

    // Whether any feature at all is meant for this host (switched on or not).
    // A service host skips plugins without one.
    public bool HasFeaturesFor(PluginHostMode host) => _features.Any(feature => feature.Host == host);

    // The entities of the active features.
    protected IEntity[] GetActiveEntities() => ActiveFeatures.SelectMany(feature => feature.GetEntities()).ToArray();

    // Called from the plugin's constructor, in the order the features should
    // appear in the settings.
    protected T AddFeature<T>(T feature) where T : PluginFeature
    {
        ArgumentNullException.ThrowIfNull(feature);

        if (_features.Any(existing => existing.Code == feature.Code))
            throw new ArgumentException($"Plugin '{PluginCode}' already has a feature '{feature.Code}'.", nameof(feature));

        feature.Attach(this);
        _features.Add(feature);

        return feature;
    }

    // The user's switch for one feature. Applied by the host while the plugin
    // is disabled; takes effect with the next EnableAsync.
    public void SetFeatureEnabled(string code, bool enabled)
    {
        var feature = _features.FirstOrDefault(f => f.Code == code)
            ?? throw new ArgumentException($"Plugin '{PluginCode}' has no feature '{code}'.", nameof(code));

        if (IsEnabled)
            throw new InvalidOperationException($"Plugin '{PluginCode}' is enabled; switch features while it is disabled.");

        feature.IsEnabled = enabled;
    }

    protected void RegisterTool(PluginTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (_tools.Any(t => t.Name == tool.Name))
            throw new ArgumentException($"Plugin '{PluginCode}' already has a tool named '{tool.Name}'.", nameof(tool));

        _tools.Add(tool);
    }

    public async Task<string> InvokeToolAsync(string name, IReadOnlyDictionary<string, string>? parameters = null)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == name)
            ?? throw new ArgumentException($"Plugin '{PluginCode}' has no tool named '{name}'.", nameof(name));

        return await tool.Handler(parameters ?? new Dictionary<string, string>());
    }

    public async Task SetConfigAsync(IPluginConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        await _lifecycleLock.WaitAsync();

        try
        {
            Config = config;
            OnConfigured();

            // Every feature, active or not: their entities follow the config,
            // and a feature switched on later must not start from stale ones.
            foreach (var feature in _features)
                feature.OnConfigured(config);

            if (IsEnabled)
            {
                await StopAsync();
                await StartAsync();
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task EnableAsync(HomeAssistantClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        await _lifecycleLock.WaitAsync();

        try
        {
            if (IsEnabled)
                return;

            if (Config == null)
                throw new PluginNotInitializedException($"Plugin '{PluginCode}' has no configuration. Call SetConfigAsync first.");

            if (!CanEnable)
                throw new PluginNotInitializedException($"Plugin '{PluginCode}' is missing required settings and stays disabled.");

            Client = client;

            // Publish discovery + wire up MQTT topics for the active features'
            // entities, then start the plugin and its features and publish the
            // initial availability.
            _registeredEntities = GetActiveEntities();
            await client.RegisterEntitiesAsync(_registeredEntities);
            await StartAsync();
            await PublishInitialAvailabilityAsync();
            IsEnabled = true;
        }
        finally
        {
            _lifecycleLock.Release();
        }

        EnabledChanged?.Invoke(this, true);
    }

    public async Task DisableAsync()
    {
        await _lifecycleLock.WaitAsync();

        try
        {
            if (!IsEnabled)
                return;

            await SetEntitiesAvailabilityAsync(false);
            await StopAsync();

            foreach (var entity in _registeredEntities)
                entity.Detach();

            _registeredEntities = [];
            IsEnabled = false;
        }
        finally
        {
            _lifecycleLock.Release();
        }

        EnabledChanged?.Invoke(this, false);
    }

    // For a plugin the user switched off: keeps its entities in Home Assistant
    // as unavailable without starting the plugin. Needs the config set first —
    // that is what decides which entities the plugin has. Only the features
    // that are switched on are published: a switched-off feature has no
    // entities in Home Assistant either way.
    public async Task PublishDisabledAsync(HomeAssistantClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        await _lifecycleLock.WaitAsync();

        try
        {
            if (IsEnabled)
                throw new InvalidOperationException($"Plugin '{PluginCode}' is enabled.");

            await client.PublishUnavailableEntitiesAsync(GetActiveEntities());
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    // Plugin first, then its features: features build on what the plugin
    // starts (a connection), and stop before it goes away.
    private async Task StartAsync()
    {
        await OnEnableAsync();

        foreach (var feature in ActiveFeatures)
            await feature.OnEnableAsync();
    }

    private async Task StopAsync()
    {
        foreach (var feature in ActiveFeatures.Reverse())
            await feature.OnDisableAsync();

        await OnDisableAsync();
    }

    // For work started from an event a monitor or client raises on its own
    // thread, where there is nobody to await it — typically publishing a new
    // state. A publish fails while the broker is reconnecting, and a bare
    // "_ = task" would drop that failure silently; this logs it instead.
    protected void FireAndForget(Task task, [CallerArgumentExpression(nameof(task))] string? operation = null)
    {
        _ = ObserveAsync(task, operation);
    }

    internal void FireAndForgetFromFeature(Task task, string? operation) => _ = ObserveAsync(task, operation);

    internal ILogger<T> CreateFeatureLogger<T>() => CreateLogger<T>();

    private async Task ObserveAsync(Task task, string? operation)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Cancelled on purpose (disable, shutdown) — not a failure.
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "{PluginCode}: {Operation} failed", PluginCode, operation);
        }
    }

    // Called at the end of EnableAsync to publish the availability of the
    // active entities. Defaults to available; plugins whose availability
    // depends on external state (e.g. a running process) can override this.
    protected virtual Task PublishInitialAvailabilityAsync()
    {
        return SetEntitiesAvailabilityAsync(true);
    }

    // The except entities are left untouched — e.g. a plugin status sensor
    // that must stay available while the controls go offline. Entities of
    // features that are not active are never touched.
    protected async Task SetEntitiesAvailabilityAsync(bool available, params IEntity[] except)
    {
        foreach (var entity in GetActiveEntities())
        {
            if (!except.Contains(entity))
                await entity.SetAvailableAsync(available);
        }
    }

    // For plugins whose entities only work while connected to an app: they
    // all follow the connection, while the status sensor stays available and
    // reports it. Safe to call when the status feature is switched off — its
    // sensor is then simply not attached.
    protected async Task PublishConnectionStateAsync(ConnectionStatusFeature status, bool connected)
    {
        await SetEntitiesAvailabilityAsync(connected, status.Sensor);
        await status.Sensor.SetAvailableAsync(true);
        await status.Sensor.SetStateAsync(connected);
    }

    protected virtual void OnConfigured() { }

    // Shared runtime state of the plugin (a connection, a process monitor).
    // Plugins whose features carry everything themselves keep the defaults.
    protected virtual Task OnEnableAsync() => Task.CompletedTask;

    protected virtual Task OnDisableAsync() => Task.CompletedTask;
}

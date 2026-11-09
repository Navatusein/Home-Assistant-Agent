using System.Diagnostics;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Settings;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Plugin.Base.Config;
using HomeAssistant.Connector;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog.Events;

namespace HomeAssistant.Agent.Core.Services;

// Applies the settings to the running agent: connects the Home Assistant
// client and configures and enables the plugins, then cleans up the discovery
// the run no longer publishes. Shared by the desktop tray app and the Windows
// service; the host mode (from the plugin manager) decides which features run
// and under which MQTT identity the client connects.
//
// Storage is elsewhere: SettingsStore for Settings.json, DiscoveryLedger for
// what was published, GeneralSettings/PluginSettings for what the values mean.
public class AgentService
{
    // Appended to the desktop identity so the service's MQTT session, topics
    // and discovery configs never collide with the desktop agent's. The HA
    // device identifier stays shared, so both entity sets land on one device.
    private const string ServiceDeviceSuffix = "-svc";
    private const string ServicePrefixSuffix = "_svc";

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _applyLock = new(1, 1);
    private CancellationTokenSource? _retryCancellation;
    private readonly PluginManager _pluginManager;
    private readonly PluginHostMode _hostMode;
    private readonly IAutoStart? _autoStart;
    private readonly SettingsStore _settingsStore;
    private readonly DiscoveryLedger _ledger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    private HomeAssistantClient? _client;

    // The broker the live client is connected to. Moving to a different broker
    // leaves the old one's retained discovery behind, and the new connection
    // cannot reach it — that is the one case the diff below cannot cover.
    private string? _connectedBroker;
    private int _connectedPort;

    // The host mode comes from the plugin manager, which already hands it to
    // every plugin: one source, so the MQTT identity and the entity sets can
    // never disagree about where they run. The discovery records live next to
    // the settings unless told otherwise.
    public AgentService(PluginManager pluginManager,
        IAutoStart? autoStart = null,
        SettingsStore? settingsStore = null,
        DiscoveryLedger? ledger = null,
        ILoggerFactory? loggerFactory = null)
    {
        _pluginManager = pluginManager;
        _hostMode = pluginManager.HostMode;
        _autoStart = autoStart;
        _settingsStore = settingsStore ?? new SettingsStore(AgentPaths.SettingsFile);
        _ledger = ledger ?? new DiscoveryLedger(Path.GetDirectoryName(_settingsStore.FilePath) ?? AgentPaths.DataDirectory);
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _logger = _loggerFactory.CreateLogger<AgentService>();
    }

    // As stored. A plugin's "config" stays raw: what its keys mean is up to
    // the plugin.
    public AgentSettings Settings { get; private set; } = new();

    public GeneralSettings General => GeneralSettings.FromValues(Settings.GetSection(GeneralSettings.Section));

    public bool IsConnected => _client?.IsConnected ?? false;

    // The MQTT connection came up or went down — including the automatic
    // reconnects in between applies. Fires on a background thread.
    public event Action<bool>? ConnectionChanged;

    public void LoadSettings()
    {
        Settings = _settingsStore.Load();
    }

    public void SaveSettings(AgentSettings settings)
    {
        Settings = settings;
        _settingsStore.Save(settings);
    }

    // Applies the current settings: reconnects the MQTT client and
    // reconfigures + re-enables the plugins. Returns human-readable problems;
    // an empty list means everything applied cleanly. With clearDiscovery the
    // retained discovery configs are dropped before the plugins re-register,
    // so entities removed by the new settings disappear from Home Assistant.
    public async Task<IReadOnlyList<string>> ApplySettingsAsync(bool clearDiscovery = false)
    {
        // Before the lock, so a retry waiting for it steps aside.
        StopRetry();

        await _applyLock.WaitAsync();

        var stopwatch = Stopwatch.StartNew();

        try
        {
            ApplyLogLevels();

            _logger.LogInformation("Applying settings{Clear}", clearDiscovery ? " (clearing discovery)" : string.Empty);

            var errors = await ApplySettingsCoreAsync(clearDiscovery);

            if (errors.Count == 0)
                _logger.LogInformation("Settings applied in {Elapsed} ms", stopwatch.ElapsedMilliseconds);
            else
                _logger.LogWarning("Settings applied in {Elapsed} ms with {Count} problem(s)", stopwatch.ElapsedMilliseconds, errors.Count);

            return errors;
        }
        finally
        {
            _applyLock.Release();
        }
    }

    // Graceful shutdown for application exit: disables the plugins and closes
    // the MQTT connection so Home Assistant sees the device go offline
    // immediately instead of waiting for the broker's last-will timeout.
    public async Task ShutdownAsync()
    {
        StopRetry();

        await _applyLock.WaitAsync();

        try
        {
            _logger.LogInformation("Shutting down");

            foreach (var plugin in _pluginManager.Plugins)
            {
                try
                {
                    await plugin.DisableAsync();
                }
                catch (Exception ex)
                {
                    // Shutting down anyway — only worth a note.
                    _logger.LogWarning(ex, "{PluginCode} failed to stop", plugin.PluginCode);
                }
            }

            await DropClientAsync(clearDiscovery: false);
        }
        catch (Exception ex)
        {
            // The broker may already be gone; exit anyway.
            _logger.LogWarning(ex, "MQTT disconnect failed during shutdown");
        }
        finally
        {
            _applyLock.Release();
        }
    }

    // Levels take effect before anything else is applied, so raising one to
    // chase a problem already covers this very apply.
    private void ApplyLogLevels()
    {
        var defaultLevel = General.LogLevel;
        var pluginLevels = new Dictionary<string, LogEventLevel>();

        foreach (var plugin in _pluginManager.Plugins)
        {
            if (GetPluginSettings(plugin).GetLogLevel() is { } pluginLevel)
                pluginLevels[plugin.PluginCode] = pluginLevel;
        }

        AgentLogging.Levels.Apply(defaultLevel, pluginLevels);

        _logger.LogInformation("Log level {Level}{Overrides}",
            LogLevels.ToName(defaultLevel),
            pluginLevels.Count == 0
                ? string.Empty
                : " (" + string.Join(", ", pluginLevels.Select(pair => $"{pair.Key}: {LogLevels.ToName(pair.Value)}")) + ")");
    }

    private async Task<IReadOnlyList<string>> ApplySettingsCoreAsync(bool clearDiscovery)
    {
        var errors = new List<string>();
        var general = General;

        // Take everything down first: plugins announce their entities as
        // unavailable while the old connection is still alive.
        foreach (var plugin in _pluginManager.Plugins)
        {
            try
            {
                await plugin.DisableAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{PluginCode} failed to stop", plugin.PluginCode);
                errors.Add($"{plugin.PluginName}: failed to stop — {ex.Message}");
            }
        }

        if (_client != null)
        {
            try
            {
                // Retained discovery lives on the broker that holds it. When the
                // settings point somewhere else, the new connection can never
                // clean it up, so wipe it here while the old client still can.
                // Everything else is handled afterwards by the topic diff, which
                // covers a renamed device just as well.
                var brokerChanged = !string.Equals(general.MqttBroker, _connectedBroker, StringComparison.OrdinalIgnoreCase)
                    || general.MqttPort != _connectedPort;

                if (brokerChanged)
                    _logger.LogInformation("Broker changed from {OldBroker}:{OldPort} — clearing the old broker's discovery",
                        _connectedBroker, _connectedPort);

                await DropClientAsync(clearDiscovery || brokerChanged);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT disconnect failed");
                errors.Add($"MQTT disconnect failed: {ex.Message}");
                _client = null;
            }
        }

        if (_autoStart != null)
        {
            try
            {
                _autoStart.Apply(general.AutoStart);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update the Windows startup entry");
                errors.Add($"Failed to update the Windows startup entry: {ex.Message}");
            }
        }

        if (!general.CanConnect)
        {
            _logger.LogWarning("Device name or MQTT host is not set — not connecting");
            errors.Add("Set the device name and MQTT host in the settings to connect.");
            return errors;
        }

        var error = await ConnectAndStartAsync(general, clearDiscovery, errors);

        if (error != null)
        {
            errors.Add(error);
            StartRetry(general, clearDiscovery);
        }

        return errors;
    }

    // Connects a new client and starts the plugins on it. Returns why the
    // connection failed, or null once it is up (plugin problems go to errors).
    private async Task<string?> ConnectAndStartAsync(GeneralSettings general, bool clearDiscovery, List<string> errors,
        bool quiet = false)
    {
        var client = CreateClient(general);
        var broker = general.MqttBroker!;
        var port = general.MqttPort;

        try
        {
            if (!await client.ConnectToMqttAsync(broker, port, general.MqttUser, general.MqttPassword))
                return $"MQTT broker {broker}:{port} refused the connection.";

            if (clearDiscovery)
                await client.ClearDiscoveryAsync();

            await client.PublishAvailabilityAsync();
        }
        catch (Exception ex)
        {
            if (quiet)
                _logger.LogDebug("MQTT connection to {Broker}:{Port} failed: {Message}", broker, port, ex.Message);
            else
                _logger.LogError(ex, "MQTT connection to {Broker}:{Port} failed", broker, port);

            // The broker may have dropped us between connect and publish.
            await DisconnectQuietlyAsync(client);

            return $"MQTT connection to {broker}:{port} failed: {ex.Message}";
        }

        _client = client;
        _connectedBroker = broker;
        _connectedPort = port;

        // Subscribed after connecting: the connect itself is reported here, so
        // a failed attempt never announces a connection.
        client.ConnectionChanged += Client_OnConnectionChanged;
        ConnectionChanged?.Invoke(true);

        foreach (var plugin in _pluginManager.Plugins)
            await StartPluginAsync(plugin, client, errors);

        // A full clear just removed everything, so there is nothing stale left
        // to delete — only the ledger needs refreshing.
        await SyncDiscoveryAsync(client, removeStale: !clearDiscovery, errors);

        return null;
    }

    // The client reconnects by itself only after a first successful connect.
    // A broker that is down when the settings are applied (boot, a Home
    // Assistant restart) is retried here, with the same settings, until it
    // answers or the next apply/shutdown takes over.
    private void StartRetry(GeneralSettings general, bool clearDiscovery)
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref _retryCancellation, cancellation)?.Cancel();

        _ = RetryLoopAsync(general, clearDiscovery, cancellation.Token);
    }

    private void StopRetry() => Interlocked.Exchange(ref _retryCancellation, null)?.Cancel();

    private async Task RetryLoopAsync(GeneralSettings general, bool clearDiscovery, CancellationToken cancellationToken)
    {
        var delay = InitialRetryDelay;
        var attempts = 0;

        _logger.LogInformation("Retrying the MQTT connection in the background");

        try
        {
            while (true)
            {
                await Task.Delay(delay, cancellationToken);
                await _applyLock.WaitAsync(cancellationToken);

                try
                {
                    // An apply or shutdown that queued up behind this attempt
                    // owns the connection now.
                    cancellationToken.ThrowIfCancellationRequested();

                    attempts++;

                    var errors = new List<string>();

                    if (await ConnectAndStartAsync(general, clearDiscovery, errors, quiet: true) == null)
                    {
                        _logger.LogInformation("MQTT connected after {Attempts} retry attempt(s){Problems}", attempts,
                            errors.Count == 0 ? string.Empty : $", {errors.Count} problem(s)");
                        return;
                    }
                }
                finally
                {
                    _applyLock.Release();
                }

                delay = delay >= MaxRetryDelay ? MaxRetryDelay : delay * 2;
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a new apply or shutdown.
        }
    }

    private async Task DisconnectQuietlyAsync(HomeAssistantClient client)
    {
        try
        {
            await client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Closing the failed MQTT connection: {Message}", ex.Message);
        }
    }

    private HomeAssistantClient CreateClient(GeneralSettings general)
    {
        var slug = GetDeviceSlug(general.DeviceName!);
        var prefix = Slugify(general.DeviceName!, '_');
        var isService = _hostMode == PluginHostMode.Service;

        return new HomeAssistantClient
        {
            MqttDevice = isService ? slug + ServiceDeviceSuffix : slug,
            MqttRootTopic = general.RootTopic,
            DeviceIdPrefix = isService ? prefix + ServicePrefixSuffix : prefix,
            DeviceIdentifier = isService ? slug : null,
            DeviceName = general.DeviceName!,
            DeviceManufacturer = "Navatusein",
            DeviceModel = "Home Assistant Agent",
            DeviceSoftwareVersion = typeof(AgentService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            LoggerFactory = _loggerFactory,
        };
    }

    // Disconnects the current client, if any, after optionally clearing its
    // device's discovery.
    private async Task DropClientAsync(bool clearDiscovery)
    {
        var client = _client;

        if (client == null)
            return;

        _client = null;
        _connectedBroker = null;

        // Unsubscribed first, so the drop is reported exactly once — below,
        // whether or not the client was still connected.
        client.ConnectionChanged -= Client_OnConnectionChanged;

        try
        {
            if (clearDiscovery)
                await client.ClearDiscoveryAsync();

            await client.DisconnectAsync();
        }
        finally
        {
            ConnectionChanged?.Invoke(false);
        }
    }

    private void Client_OnConnectionChanged(bool connected) => ConnectionChanged?.Invoke(connected);

    private async Task StartPluginAsync(BasePlugin plugin, HomeAssistantClient client, List<string> errors)
    {
        var settings = GetPluginSettings(plugin);

        try
        {
            // A plugin without a single feature for this host has nothing to
            // do here (most plugins, in the service).
            if (!plugin.HasFeaturesFor(_hostMode))
                return;

            ApplyFeatureSwitches(plugin, settings);

            // Every feature for this host switched off: nothing to run, and
            // nothing to register — the diff removes the entities from Home
            // Assistant.
            if (!plugin.ActiveFeatures.Any())
            {
                _logger.LogInformation("{PluginCode} has no features switched on — skipped", plugin.PluginCode);
                return;
            }

            var config = plugin.CreateConfig(new ConfigValues(settings.Config));

            // Not configured yet — leave the plugin disabled.
            if (config == null)
            {
                _logger.LogInformation("{PluginCode} is not configured — skipped", plugin.PluginCode);
                return;
            }

            await plugin.SetConfigAsync(config);

            // Switched off by the user — its entities stay in Home Assistant as
            // unavailable rather than being removed.
            if (!settings.Enabled)
            {
                await plugin.PublishDisabledAsync(client);
                _logger.LogInformation("{PluginCode} is switched off — entities published as unavailable", plugin.PluginCode);
                return;
            }

            var published = client.PublishedDiscoveryTopics.Count;

            await plugin.EnableAsync(client);

            _logger.LogInformation("{PluginCode} enabled with {Count} entities",
                plugin.PluginCode, client.PublishedDiscoveryTopics.Count - published);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{PluginCode} failed to start", plugin.PluginCode);
            errors.Add($"{plugin.PluginName}: {ex.Message}");
        }
    }

    private void ApplyFeatureSwitches(BasePlugin plugin, PluginSettings settings)
    {
        foreach (var feature in plugin.Features)
            plugin.SetFeatureEnabled(feature.Code, settings.IsFeatureEnabled(feature));

        var off = plugin.Features
            .Where(feature => feature.Host == _hostMode && !feature.IsEnabled)
            .Select(feature => feature.Code)
            .ToArray();

        if (off.Length > 0)
            _logger.LogInformation("{PluginCode}: switched off {Features}", plugin.PluginCode, string.Join(", ", off));
    }

    // Deletes the discovery configs that this run no longer publishes, then
    // records what it did publish for the next one.
    //
    // This replaces clearing the device's discovery on every apply. The diff
    // covers the cases that motivated the clear — an entry removed from a list,
    // a plugin left unconfigured, a plugin removed, a renamed device (its topics
    // carry the old slug and so read as gone) — without making every entity in
    // Home Assistant blink on an unrelated settings change.
    private async Task SyncDiscoveryAsync(HomeAssistantClient client, bool removeStale, List<string> errors)
    {
        var published = client.PublishedDiscoveryTopics.ToHashSet(StringComparer.Ordinal);

        try
        {
            var removed = 0;

            if (removeStale)
            {
                var stale = _ledger.Read(client.MqttDevice).Except(published).ToArray();

                if (stale.Length > 0)
                    await client.RemoveDiscoveryAsync(stale);

                removed = stale.Length;
            }

            _ledger.Write(client.MqttDevice, published);

            _logger.LogInformation("Discovery: {Published} published, {Removed} removed", published.Count, removed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update the discovery record");
            errors.Add($"Failed to update the discovery record: {ex.Message}");
        }
    }

    private PluginSettings GetPluginSettings(BasePlugin plugin) => Settings.GetPlugin(plugin.PluginCode);

    // The MQTT device id a display name produces. Exposed so the settings UI
    // can preview the identity before the name is saved, without duplicating
    // the slug rules.
    public static string GetDeviceSlug(string deviceName) => Slugify(deviceName, '-');

    // A display name as an MQTT/Home Assistant id ("My Laptop" -> "my-laptop").
    private static string Slugify(string value, char separator) =>
        Slug.ToObjectId(value, separator, fallback: "home-assistant-agent");
}

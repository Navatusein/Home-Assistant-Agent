using HomeAssistant.Agent.Core;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Core.Storage;
using HomeAssistant.Agent.Plugin.Base;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Service;

// Runs the agent core inside the Windows service: loads the plugins, applies
// the settings shared with the desktop app, and keeps the MQTT connection
// alive across broker outages (the service starts at boot, typically before
// the network and the broker are reachable).
public class AgentWorker(
    PluginManager pluginManager,
    AgentService agentService,
    PersistentStore store,
    ILogger<AgentWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan SettingsDebounce = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _applyLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        pluginManager.LoadPlugins();

        AgentLogging.LogStartup(logger, PluginHostMode.Service, pluginManager);

        // Stored plugin state in the current key format before any plugin runs.
        await StorageMigrations.ApplyAsync(store, logger);

        using var watcher = WatchSettings(stoppingToken);

        // A broker unreachable at boot is retried by AgentService, and drops
        // after that by the MQTT client's own reconnect loop.
        await ApplySettingsAsync();

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // The service is stopping.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Bounded so a dead broker can't stall the service stop forever.
        await Task.WhenAny(agentService.ShutdownAsync(), Task.Delay(ShutdownTimeout, CancellationToken.None));
        await base.StopAsync(cancellationToken);
    }

    // Reloads and reapplies the settings when the desktop app saves them.
    // Events are debounced (editors fire several per save) and the file is
    // re-read with retries because the writer may still hold it open.
    private FileSystemWatcher WatchSettings(CancellationToken stoppingToken)
    {
        var settingsFile = AgentPaths.SettingsFile;

        var watcher = new FileSystemWatcher(Path.GetDirectoryName(settingsFile)!, Path.GetFileName(settingsFile))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        };

        CancellationTokenSource? debounce = null;

        void OnChanged(object sender, FileSystemEventArgs e)
        {
            var previous = Interlocked.Exchange(ref debounce,
                CancellationTokenSource.CreateLinkedTokenSource(stoppingToken));
            previous?.Cancel();
            previous?.Dispose();

            var token = debounce.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(SettingsDebounce, token);
                    logger.LogInformation("Settings.json changed — reapplying.");
                    // No clearDiscovery: the discovery diff in AgentService
                    // removes exactly what this apply stopped publishing, so
                    // a save in the desktop app no longer makes every service
                    // entity blink in Home Assistant.
                    await ApplySettingsAsync();
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a newer change or the service is stopping.
                }
            }, CancellationToken.None);
        }

        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += (s, e) => OnChanged(s, e);
        watcher.EnableRaisingEvents = true;

        return watcher;
    }

    private async Task ApplySettingsAsync()
    {
        await _applyLock.WaitAsync();

        try
        {
            LoadSettingsWithRetry();

            // AgentService logs the outcome and every problem itself.
            await agentService.ApplySettingsAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply settings.");
        }
        finally
        {
            _applyLock.Release();
        }
    }

    private void LoadSettingsWithRetry()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                agentService.LoadSettings();
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                // The desktop app may still be writing the file.
                Thread.Sleep(200);
            }
        }
    }
}

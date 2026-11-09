using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Core.Storage;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.ViewModels;
using HomeAssistant.Agent.ViewModels.Pages;
using HomeAssistant.Agent.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Services;

// Brings the agent up once Avalonia runs: reads the settings, puts up the tray
// icon, connects and enables the plugins. The window is built only when it is
// first asked for, from the tray, a notification or a second launch.
public sealed class ApplicationHostService(
    IServiceProvider services,
    PluginManager pluginManager,
    AgentService agentService,
    PersistentStore store,
    TrayIconService trayIcon,
    DesktopNotifier notifier,
    UpdateService updates,
    ISingleInstance singleInstance,
    MqttConnectionNotifier connectionNotifier,
    NavigationService navigation,
    ILogger<ApplicationHostService> logger) : IHostedService
{
    // Bounded so a dead broker can't stall the exit forever.
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);

    private MainWindow? _window;
    private bool _exiting;

    // Kept so the handler stays registered for the lifetime of the app.
    private PosixSignalRegistration? _terminate;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Reading the settings must happen before the window can be opened:
        // the settings page builds its sections from AgentService.Settings.
        await LoadAgentStateAsync();

        HandleActivation();

        connectionNotifier.Start();

        // Applying them talks to the broker, so it runs after the tray icon
        // is up rather than delaying it.
        await ApplySettingsAsync();

        // Checks on its own schedule from here on, if the user has it on.
        updates.Start();
    }

    // Disables the plugins and closes the MQTT connection so Home Assistant
    // sees the device go offline immediately instead of waiting for the
    // broker's last-will timeout.
    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.WhenAny(agentService.ShutdownAsync(), Task.Delay(ShutdownTimeout, cancellationToken));

    private void HandleActivation()
    {
        trayIcon.OpenRequested += ShowWindow<DashboardViewModel>;
        trayIcon.SettingsRequested += ShowWindow<SettingsViewModel>;
        trayIcon.UpdateRequested += ShowWindow<DashboardViewModel>;
        trayIcon.RestartRequested += Restart;
        trayIcon.ExitRequested += () =>
        {
            logger.LogInformation("Exit requested from the tray");
            _ = ExitAsync();
        };

        notifier.Clicked += ShowWindow<DashboardViewModel>;
        notifier.ShowInWindowRequested += (title, message, warning) => _window?.ShowNotification(title, message, warning);

        // Launching the agent again brings this one forward instead of
        // starting a second copy. Fires on a thread-pool thread.
        singleInstance.ActivationRequested += () =>
        {
            logger.LogInformation("A second launch asked for the window");
            Dispatcher.UIThread.Post(ShowWindow<DashboardViewModel>);
        };

        // A --restart launch is waiting for this process to release the
        // session; exiting the same way the tray does keeps it orderly.
        singleInstance.ShutdownRequested += () =>
        {
            logger.LogInformation("A restart launch asked this instance to exit");
            Dispatcher.UIThread.Post(() => _ = ExitAsync());
        };

        // SIGTERM (kill, launchctl, a shutdown that does not go through Apple
        // Events) ends the agent the orderly way too: offline announced to
        // Home Assistant first. The default would end the process outright.
        if (!OperatingSystem.IsWindows())
        {
            _terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                logger.LogInformation("SIGTERM received — exiting");
                Dispatcher.UIThread.Post(() => _ = ExitAsync());
            });
        }

        trayIcon.Show();
    }

    // The replacement asks this instance to exit as soon as it is up, which
    // lands on ShutdownRequested above — so a failed launch simply leaves
    // the running agent alone.
    private void Restart()
    {
        try
        {
            logger.LogInformation("Restarting the agent");
            AgentRestart.Start();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start the replacement instance");
        }
    }

    private void ShowWindow<TPage>() where TPage : class
    {
        if (_exiting)
            return;

        if (_window == null)
        {
            _window = new MainWindow { DataContext = services.GetRequiredService<MainWindowViewModel>() };

            // Dialogs (file pickers, confirmations) are owned by this window.
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = _window;
        }

        navigation.Navigate<TPage>();

        _window.Show();

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
    }

    // Drop the icon, let the agent announce itself offline, then leave. The
    // explicit exit is not optional: libraries the plugins pull in keep
    // foreground threads alive, so the process outlives the UI without it.
    public async Task ExitAsync()
    {
        if (_exiting)
            return;

        _exiting = true;
        trayIcon.Dispose();
        _window?.Hide();

        logger.LogInformation("Exiting");

        // Bounded: a shutdown stuck behind a connect attempt must not keep the
        // agent around — the broker's last will marks the device offline then.
        var shutdown = agentService.ShutdownAsync();

        if (await Task.WhenAny(shutdown, Task.Delay(ShutdownTimeout)) != shutdown)
            logger.LogWarning("The agent did not shut down within {Timeout}; exiting anyway", ShutdownTimeout);

        await Serilog.Log.CloseAndFlushAsync();

        Environment.Exit(0);
    }

    // Before any plugin is enabled: stored plugin state is brought up to
    // the current key format, then the settings are read.
    private async Task LoadAgentStateAsync()
    {
        try
        {
            // A plugin that silently fails to load is the single most
            // confusing startup problem, so it is logged before anything runs.
            AgentLogging.LogStartup(logger, PluginHostMode.Desktop, pluginManager);

            await StorageMigrations.ApplyAsync(store, logger);

            agentService.LoadSettings();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load the agent state");
        }
    }

    // Connects to MQTT and enables the configured plugins.
    private async Task ApplySettingsAsync()
    {
        try
        {
            // AgentService logs every problem itself, with the exception.
            await agentService.ApplySettingsAsync();

            logger.LogInformation("Agent started. MQTT connected: {IsConnected}. Enabled plugins: {Plugins}",
                agentService.IsConnected,
                string.Join(", ", pluginManager.Plugins.Where(p => p.IsEnabled).Select(p => p.PluginCode)));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start the agent");
        }
    }
}

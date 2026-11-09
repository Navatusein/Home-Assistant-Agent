using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HomeAssistant.Agent.Core;
using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Core.Storage;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Services;
using HomeAssistant.Agent.ViewModels;
using HomeAssistant.Agent.ViewModels.Pages;
using HomeAssistant.Agent.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace HomeAssistant.Agent;

public partial class App : Application
{
    private IHost? _host;

    // Picked in Program.Main, where the single-instance lock was taken.
    public static PlatformServices? Platform { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.Exception, "Unhandled UI exception");
            e.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Platform != null)
        {
            _host = BuildHost(Platform);

            // Signing out or shutting the system down ends the lifetime
            // without the tray's Exit; the agent still goes offline first.
            desktop.Exit += (_, _) => StopHost();

            _ = StartHostAsync(_host);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartHostAsync(IHost host)
    {
        try
        {
            await host.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "The agent failed to start");
        }
    }

    private void StopHost()
    {
        if (_host == null)
            return;

        // Off the UI thread: the lifetime is ending and its loop no longer
        // runs the continuations an await here would need.
        Task.Run(() => _host.StopAsync()).Wait(ApplicationHostService.ShutdownTimeout + TimeSpan.FromSeconds(1));

        _host.Dispose();
        _host = null;
    }

    // The .NET Generic Host provides dependency injection and logging.
    private static IHost BuildHost(PlatformServices platform) => Host
        .CreateDefaultBuilder()
        // Auto-start launches the app with C:\Windows\System32 as the
        // working directory, so anchor the host to the install folder
        // instead of inheriting whatever CWD it was started with.
        .UseContentRoot(AppContext.BaseDirectory)
        .ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog(Log.Logger);
        })
        .ConfigureServices((_, services) =>
        {
            services.AddHostedService<ApplicationHostService>();

            // Owns the tray icon: the window starts hidden, so this is what
            // keeps the application reachable.
            services.AddSingleton<TrayIconService>();
            services.AddSingleton<DesktopNotifier>();
            services.AddSingleton<IUserNotifier>(provider => provider.GetRequiredService<DesktopNotifier>());
            services.AddSingleton<MqttConnectionNotifier>();

            // Agent and plugin updates from the catalogs.
            services.AddSingleton<UpdateService>();

            // What the operating system provides. The single-instance lock is
            // already acquired — registered so the host service can react to
            // a second launch asking to be shown.
            services.AddSingleton(platform);
            services.AddSingleton(platform.AutoStart);
            services.AddSingleton(platform.BackgroundService);
            services.AddSingleton(platform.Elevation);
            services.AddSingleton(platform.SingleInstance);
            services.AddSingleton(platform.Shell);

            // The plugins' persistent state (pairing and auth tokens). The
            // service keeps its own file: both processes rewrite theirs whole.
            services.AddSingleton(_ => PersistentStore.Load(AgentPaths.GetStorageFile(PluginHostMode.Desktop)));

            // The agent itself: plugins are discovered once at startup and
            // initialized with their context, and AgentService owns the
            // MQTT connection and the settings file.
            services.AddSingleton(provider =>
            {
                var pluginManager = new PluginManager(
                    PluginHostMode.Desktop,
                    provider.GetRequiredService<ILoggerFactory>(),
                    provider.GetRequiredService<PersistentStore>());

                pluginManager.LoadPlugins();
                return pluginManager;
            });

            services.AddSingleton(provider => new AgentService(
                provider.GetRequiredService<PluginManager>(),
                autoStart: platform.AutoStart,
                loggerFactory: provider.GetRequiredService<ILoggerFactory>()));

            // Settings sections describing the application itself, in order.
            services.AddSingleton<WindowsServiceViewModel>();
            services.AddSingleton<ISettingsSection, GeneralSettingsViewModel>();

            // The window and its pages.
            services.AddSingleton<NavigationService>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<DashboardViewModel>();
            services.AddSingleton<LogsViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<PluginSettingsViewModel>();
        }).Build();
}

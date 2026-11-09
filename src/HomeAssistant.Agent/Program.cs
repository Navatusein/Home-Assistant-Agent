using Avalonia;
using Avalonia.Controls;
using HomeAssistant.Agent.Core;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Logging;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Services;
using Serilog;

namespace HomeAssistant.Agent;

internal static class Program
{
    // The instance being replaced announces itself offline and closes its
    // MQTT session first (bounded by ApplicationHostService.ShutdownTimeout),
    // so this has to allow for more than that.
    private static readonly TimeSpan RestartTimeout = TimeSpan.FromSeconds(15);

    // "--shutdown": asks the agent running in this session to exit — the same
    // orderly exit as the tray's (offline announced to Home Assistant) — and
    // waits for it, then leaves without starting anything. The installer
    // runs this before replacing the files.
    private const string ShutdownArgument = "--shutdown";

    // Everything that decides whether this process becomes the agent runs
    // before Avalonia: a second launch must not load plugins, open an MQTT
    // session under the same client id or even bring up a UI.
    [STAThread]
    public static int Main(string[] args)
    {
        // The data folder holds the logs, so it must exist before them; on a
        // Mac creating it the first time needs the administrator password.
        if (OperatingSystem.IsMacOS() && !Platform.MacFirstRun.EnsureDataFolder())
            return 1;

        ConfigureLogging();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception");

        TaskScheduler.UnobservedTaskException += (_, e) =>
            Log.Error(e.Exception, "Unobserved task exception");

        PlatformServices platform;

        try
        {
            platform = PlatformServices.ForCurrentOs();
        }
        catch (PlatformNotSupportedException ex)
        {
            Log.Fatal(ex, "This operating system is not supported yet");
            Log.CloseAndFlush();
            return 1;
        }

        var singleInstance = platform.SingleInstance;

        if (HasArgument(args, ShutdownArgument))
        {
            ShutDownRunningInstance(singleInstance);
            return 0;
        }

        if (!singleInstance.TryAcquire() && !TakeOver(singleInstance, HasArgument(args, AgentRestart.Argument)))
        {
            singleInstance.Dispose();
            Log.CloseAndFlush();
            return 0;
        }

        App.Platform = platform;

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "The application crashed");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    // Also used by the XAML previewer.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        // A menu bar app on macOS: no Dock icon and no app menu of its own —
        // like the Windows build, it lives in the tray (the menu bar extra).
        .With(new MacOSPlatformOptions { ShowInDock = false, DisableDefaultApplicationMenuItems = true })
        .LogToSerilog();

    // Waits for the previous instance to release the session. On this thread,
    // not with TryAcquireAsync: a mutex belongs to the thread that took it,
    // and continuations of a blocked-on task run on the thread pool, so the
    // main thread could not release it later.
    private static readonly TimeSpan AcquireRetryInterval = TimeSpan.FromMilliseconds(100);

    private static bool AcquireWithin(ISingleInstance singleInstance, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (!singleInstance.TryAcquire())
        {
            if (DateTime.UtcNow >= deadline)
                return false;

            Thread.Sleep(AcquireRetryInterval);
        }

        return true;
    }

    private static bool HasArgument(string[] args, string argument) =>
        args.Any(arg => string.Equals(arg, argument, StringComparison.OrdinalIgnoreCase));

    // Logging backend for the host and every plugin — the setup shared with
    // the Windows service (AgentLogging), plus the in-memory buffer the
    // Logs page shows. Starts at Info; the configured level is applied with
    // the rest of the settings (AgentService.ApplySettingsAsync).
    private static void ConfigureLogging()
    {
        // The data directory (%ProgramData%\HomeAssistantAgent, or next to
        // the exe when portable) holds the logs too, so it comes first.
        AgentPaths.EnsureDataDirectory();

        AgentLogging.Configure(PluginHostMode.Desktop, configuration =>
            configuration.WriteTo.Sink(LogBuffer.Instance));
    }

    private static void ShutDownRunningInstance(ISingleInstance singleInstance)
    {
        if (singleInstance.TryAcquire())
        {
            Log.Information("Shutdown requested, but no agent is running");
        }
        else
        {
            Log.Information("Shutdown requested — asking the running instance to exit");

            singleInstance.SignalExistingInstanceShutdown();

            if (!AcquireWithin(singleInstance, RestartTimeout))
                Log.Warning("The running instance did not exit within {Timeout}", RestartTimeout);
        }

        singleInstance.Dispose();
        Log.CloseAndFlush();
    }

    // Another instance owns the session. Without --restart this launch just
    // surfaces that instance and quits (returning false); with it, the
    // running agent is asked to exit and this process waits to become the
    // one instance. Returning false means "stop starting up".
    private static bool TakeOver(ISingleInstance singleInstance, bool restart)
    {
        if (!restart)
        {
            Log.Information("Another instance is already running — bringing its window to the front");

            singleInstance.SignalExistingInstance();
            return false;
        }

        Log.Information("Restart requested — asking the running instance to exit");

        singleInstance.SignalExistingInstanceShutdown();

        if (AcquireWithin(singleInstance, RestartTimeout))
            return true;

        // Killing it would leave Home Assistant with a device that never
        // went offline, so the running instance is left as it is and this
        // launch is the one that gives up.
        Log.Error("The running instance did not exit within {Timeout} — not restarting", RestartTimeout);

        return false;
    }
}

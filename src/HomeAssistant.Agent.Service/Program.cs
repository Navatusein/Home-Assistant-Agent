using HomeAssistant.Agent.Core;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Core.Storage;
using HomeAssistant.Agent.Core.Updates;
using HomeAssistant.Agent.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

// Management verbs, run elevated by the desktop app or the installer:
// install | uninstall | start | stop | status (setup on macOS), and plugins
// (PluginStep).
ChildSignal.RestoreDefault();

if (args.Length > 0)
{
    return args[0] == PluginOperations.Verb
        ? PluginStep.Run(args)
        : ServiceManager.Run(args[0]);
}

// The same logging as the desktop app, into Logs\service-<date>.log next to
// the app's own file in the shared data directory.
// On macOS the shared data folder is made by "setup" (as root); until then
// there is nowhere to log to, so the reason goes to stderr (launchd keeps it
// in the system log) instead of an unhandled-exception crash.
try
{
    AgentPaths.EnsureDataDirectory();
}
catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
{
    Console.Error.WriteLine($"Cannot use the data folder {AgentPaths.DataDirectory}: {ex.Message}");

    if (OperatingSystem.IsMacOS())
        Console.Error.WriteLine("Run 'setup' as root first (the desktop app does it on its first start).");

    return 1;
}

var loggerFactory = AgentLogging.Configure(PluginHostMode.Service);

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger);

#if WINDOWS
    // Under the SCM this wires the service lifetime; started from a console the
    // host simply runs interactively, which doubles as the debug mode.
    builder.Services.AddWindowsService(options => options.ServiceName = BackgroundServiceInfo.ServiceName);
#endif
    // Under launchd nothing is needed: it starts the process and stops it with
    // SIGTERM, which the host's console lifetime already handles.

    // Own storage file: the desktop app writes PersistentStorage.json in the
    // same directory, and each process rewrites its file whole on save —
    // sharing one would clobber it.
    builder.Services.AddSingleton(_ => PersistentStore.Load(AgentPaths.GetStorageFile(PluginHostMode.Service)));

    builder.Services.AddSingleton(provider => new PluginManager(
        PluginHostMode.Service, loggerFactory, provider.GetRequiredService<PersistentStore>()));

    builder.Services.AddSingleton(provider =>
        new AgentService(provider.GetRequiredService<PluginManager>(), loggerFactory: loggerFactory));
    builder.Services.AddHostedService<AgentWorker>();

    builder.Build().Run();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "The service host crashed");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

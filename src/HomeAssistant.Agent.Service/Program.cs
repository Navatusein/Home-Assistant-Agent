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
// install | uninstall | start | stop | status, and plugins (PluginStep).
if (args.Length > 0)
{
    return args[0] == PluginOperations.Verb
        ? PluginStep.Run(args)
        : ServiceInstaller.Run(args[0]);
}

// The same logging as the desktop app, into Logs\service-<date>.log next to
// the app's own file in the shared data directory.
AgentPaths.EnsureDataDirectory();
var loggerFactory = AgentLogging.Configure(PluginHostMode.Service);

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger);

    // Under the SCM this wires the service lifetime; started from a console the
    // host simply runs interactively, which doubles as the debug mode.
    builder.Services.AddWindowsService(options => options.ServiceName = WindowsServiceInfo.ServiceName);

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

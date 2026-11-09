using System.Runtime.InteropServices;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Plugin.Base;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace HomeAssistant.Agent.Core.Logging;

// One logging setup for both hosts. The desktop app and the Windows service
// write side by side into the same Logs folder, each to its own file, with the
// same line format — so the two can be read (and parsed back by the Logs page)
// the same way.
//
// Serilog stays a host concern: plugins only ever see
// Microsoft.Extensions.Logging, through the ILoggerFactory in their
// PluginContext (the one Configure returns).
public static class AgentLogging
{
    // Host tag and source go into every line so a merged view of both files,
    // or a pasted excerpt, still says where each line came from:
    // 2026-10-16 12:00:00.000 [Info] [svc] windows-plugin/SessionMonitor: ...
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Lvl}] [{LogHost}] {LogSource}: {Message:lj}{NewLine}{Exception}";

    public const string HostProperty = "LogHost";

    private const long FileSizeLimit = 10 * 1024 * 1024;
    private const int RetainedFiles = 14;

    private static int _handlersRegistered;

    public static LogLevelController Levels { get; } = new();

    public static string LogsDirectory => AgentPaths.LogsDirectory;

    public static string GetHostTag(PluginHostMode host) => host == PluginHostMode.Service ? "svc" : "app";

    // Rolling files are named <prefix><date>[_NNN].log.
    public static string GetFilePrefix(PluginHostMode host) => host == PluginHostMode.Service ? "service-" : "agent-";

    // Builds the process-wide logger and starts catching exceptions nobody else
    // does; the returned factory is what the host passes on (to the plugins,
    // via PluginManager). configure lets a host add its own sinks (the desktop
    // app's in-memory buffer for the Logs page).
    public static ILoggerFactory Configure(PluginHostMode host, Action<LoggerConfiguration>? configure = null)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(Levels.MinimumLevel)
            .Enrich.FromLogContext()
            .Enrich.WithProperty(HostProperty, GetHostTag(host))
            .Enrich.With(new LevelNameEnricher(), new SourceEnricher(), new SecretMasker())
            .Filter.ByIncludingOnly(Levels.IsEnabled)
            .WriteTo.Console(outputTemplate: OutputTemplate)
            .WriteTo.File(
                Path.Combine(LogsDirectory, GetFilePrefix(host) + ".log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: RetainedFiles,
                fileSizeLimitBytes: FileSizeLimit,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: OutputTemplate);

        configure?.Invoke(configuration);

        Log.Logger = configuration.CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);

        RegisterUnhandledExceptionHandlers();

        return factory;
    }

    // Where a bug report starts: what ran, where, and with which plugins.
    public static void LogStartup(ILogger logger, PluginHostMode host, PluginManager pluginManager)
    {
        var agent = typeof(AgentLogging).Assembly.GetName().Version?.ToString(3) ?? "unknown";

        logger.LogInformation(
            "Home Assistant Agent {Version} starting as {Host}. OS {OS} ({Architecture}), .NET {Runtime}, user {User}, installed in {Folder}, data in {DataFolder}{Portable}",
            agent,
            // Strings, not the enums: Serilog quotes an enum value even with :l.
            host.ToString(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            Environment.UserName,
            AgentPaths.InstallDirectory,
            AgentPaths.DataDirectory,
            AgentPaths.IsPortable ? " (portable)" : string.Empty);

        logger.LogInformation("Loaded {Count} plugin(s) from {Directory}, plugin API {ApiVersion}",
            pluginManager.Plugins.Count, string.Join(", ", pluginManager.PluginDirectories), PluginManager.ApiVersion.ToString(2));

        foreach (var plugin in pluginManager.Plugins)
        {
            var assembly = plugin.GetType().Assembly;

            logger.LogInformation("Plugin {PluginCode} {PluginVersion} ({Assembly})",
                plugin.PluginCode,
                assembly.GetName().Version?.ToString(3) ?? "unknown",
                Path.GetFileName(assembly.Location));
        }

        foreach (var error in pluginManager.Errors)
            logger.LogError("Plugin {PluginFile} failed to load: {Error}", error.Source, error.Message);

        foreach (var skipped in pluginManager.Skipped)
            logger.LogInformation("Plugin {PluginFile} is for {Platforms} — skipped on this system",
                Path.GetFileName(skipped.Source), string.Join(", ", skipped.Platforms));
    }

    // Catches what escapes everything else:
    // - a crash on a background thread, logged before the process dies;
    // - a faulted task nobody awaited (a fire-and-forget publish, a detached
    //   event handler), which .NET otherwise drops without a trace.
    private static void RegisterUnhandledExceptionHandlers()
    {
        if (Interlocked.Exchange(ref _handlersRegistered, 1) == 1)
            return;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);

            if (e.IsTerminating)
                Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }
}

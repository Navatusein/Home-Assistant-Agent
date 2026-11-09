using System.Diagnostics;
using HomeAssistant.Agent.Core;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Core.Updates;
using HomeAssistant.Agent.Plugin.Base;
using Microsoft.Extensions.Logging;
using Serilog;

namespace HomeAssistant.Agent.Service;

// The elevated half of installing, updating or removing plugins (the desktop
// app's UpdateService is the other): run as administrator, after the desktop
// app has downloaded and verified the files and exited.
//
//   plugins [--wait <pid>] [--relaunch app]
//           [--install <file> <sha256> <code> <version> <source> <file name>]...
//           [--remove <code> <file name>]...
//
// Waits for the desktop app to be gone and stops the service — both hold the
// plugin dlls open — applies the changes, then starts the service again and
// brings the desktop app back, unelevated. The outcome is written to
// plugins.report.json for the app to show, and to the service log.
internal static class PluginStep
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    public static int Run(string[] args)
    {
        AgentPaths.EnsureDataDirectory();
        var logger = AgentLogging.Configure(PluginHostMode.Service).CreateLogger("PluginStep");

        PluginRequest request;

        try
        {
            request = PluginOperations.Parse(args);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Invalid plugin command line");
            Log.CloseAndFlush();
            return 2;
        }

        var serviceWasRunning = false;

        try
        {
            WaitForExit(request.WaitForProcess, logger);

            var status = ServiceManager.Run("status");
            serviceWasRunning = status == 0;

            if (status is 0 or 1)
                ServiceManager.Run("stop");

            PluginOperations.Apply(request, AgentPaths.InstalledPluginsDirectory, new PluginLock(PluginLock.DefaultPath));

            var message = Describe(request);
            logger.LogInformation("Plugins changed: {Changes}", message);
            PluginOperations.WriteReport(new PluginReport(true, message, DateTimeOffset.Now));

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Changing the plugins failed");
            PluginOperations.WriteReport(new PluginReport(false, $"Changing the plugins failed: {ex.Message}", DateTimeOffset.Now));

            return 1;
        }
        finally
        {
            if (serviceWasRunning)
                ServiceManager.Run("start");

            if (request.Relaunch != null)
                RelaunchDesktopApp(logger);

            Log.CloseAndFlush();
        }
    }

    private static void WaitForExit(int? processId, Microsoft.Extensions.Logging.ILogger logger)
    {
        if (processId == null)
            return;

        try
        {
            using var process = Process.GetProcessById(processId.Value);

            if (!process.WaitForExit(WaitTimeout))
                throw new TimeoutException("The desktop app did not exit; its plugins are still in use.");
        }
        catch (ArgumentException)
        {
            // Already gone.
        }

        logger.LogDebug("Desktop app {ProcessId} has exited", processId);
    }

    // Always the app next to this executable, whatever the command line said,
    // and through Explorer so that it runs as the signed-in user, not with the
    // administrator rights of this process.
    //
    // Windows only: on macOS the app runs this step and waits for it, then
    // restarts itself (UpdateService), so there is nothing to bring back.
    private static void RelaunchDesktopApp(Microsoft.Extensions.Logging.ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var app = Path.Combine(AgentPaths.InstallDirectory, "HomeAssistant.Agent.exe");

        if (!File.Exists(app))
            return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{app}\"") { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not start the desktop app again");
        }
    }

    private static string Describe(PluginRequest request)
    {
        var parts = request.Installs.Select(install => $"installed {install.Code} {install.Version}")
            .Concat(request.Removals.Select(removal => $"removed {removal.Code}"));

        return string.Join(", ", parts);
    }
}

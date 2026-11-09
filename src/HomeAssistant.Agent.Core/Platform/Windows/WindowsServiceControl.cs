using System.Runtime.Versioning;
using System.ServiceProcess;

namespace HomeAssistant.Agent.Core.Platform.Windows;

// The Windows service through the service manager. State is read here; the
// changes run the service executable's own verbs (install, start, stop,
// uninstall) elevated, since they need administrator rights.
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceControl(IElevatedLauncher elevation) : IBackgroundServiceControl
{
    private readonly string _executablePath =
        Path.Combine(AgentPaths.InstallDirectory, WindowsServiceInfo.ExecutableName);

    public BackgroundServiceState GetState()
    {
        if (!File.Exists(_executablePath))
            return BackgroundServiceState.Missing;

        // Enumerating instead of opening by name: a missing service is a normal
        // state here, not an exception.
        var services = ServiceController.GetServices();

        try
        {
            var service = services.FirstOrDefault(s => s.ServiceName == WindowsServiceInfo.ServiceName);

            return service?.Status switch
            {
                null => BackgroundServiceState.NotInstalled,
                ServiceControllerStatus.Running => BackgroundServiceState.Running,
                ServiceControllerStatus.StartPending => BackgroundServiceState.Starting,
                ServiceControllerStatus.StopPending => BackgroundServiceState.Stopping,
                ServiceControllerStatus.Stopped => BackgroundServiceState.Stopped,
                _ => BackgroundServiceState.Paused,
            };
        }
        finally
        {
            foreach (var service in services)
                service.Dispose();
        }
    }

    public async Task RunAsync(BackgroundServiceAction action)
    {
        var verb = action.ToString().ToLowerInvariant();

        using var process = elevation.Start(_executablePath, [verb])
            ?? throw new InvalidOperationException($"'{verb}' did not start");

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'{verb}' failed (exit code {process.ExitCode})");
    }
}

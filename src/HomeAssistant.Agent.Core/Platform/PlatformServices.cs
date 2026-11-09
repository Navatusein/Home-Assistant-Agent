using HomeAssistant.Agent.Core.Platform.Windows;

namespace HomeAssistant.Agent.Core.Platform;

// What the desktop host needs from the operating system, picked once for the
// one it runs on. Notifications are not here: they come from the UI's tray
// icon (IUserNotifier).
public sealed class PlatformServices
{
    public required IAutoStart AutoStart { get; init; }
    public required IBackgroundServiceControl BackgroundService { get; init; }
    public required IElevatedLauncher Elevation { get; init; }
    public required ISingleInstance SingleInstance { get; init; }
    public required IShell Shell { get; init; }

    public static PlatformServices ForCurrentOs()
    {
        if (OperatingSystem.IsWindows())
        {
            var elevation = new WindowsElevatedLauncher();

            return new PlatformServices
            {
                AutoStart = new WindowsAutoStart(),
                BackgroundService = new WindowsServiceControl(elevation),
                Elevation = elevation,
                SingleInstance = new WindowsSingleInstance(),
                Shell = new WindowsShell(),
            };
        }

        throw new PlatformNotSupportedException($"{Environment.OSVersion} is not supported yet.");
    }
}

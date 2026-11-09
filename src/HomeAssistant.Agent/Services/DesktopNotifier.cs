using System.Diagnostics;
using Avalonia.Threading;
using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Core.Platform.MacOS;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Services;

// Notifications from the desktop agent: a system toast on Windows, a
// Notification Center banner on macOS (through AppleScript). Elsewhere the
// message goes to the window if it is open; to the log in every case.
public sealed class DesktopNotifier : IUserNotifier
{
    private readonly ILogger<DesktopNotifier> _logger;

#if WINDOWS
    private readonly Platform.WindowsToasts? _toasts;
#endif

    public DesktopNotifier(ILogger<DesktopNotifier> logger)
    {
        _logger = logger;

#if WINDOWS
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            try
            {
                _toasts = new Platform.WindowsToasts();
                _toasts.Activated += () => Dispatcher.UIThread.Post(() => Clicked?.Invoke());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Windows notifications are not available");
            }
        }
#endif
    }

    // The user clicked a notification. Raised on the UI thread — on Windows
    // only: elsewhere there is no click to hear about.
#pragma warning disable CS0067
    public event Action? Clicked;
#pragma warning restore CS0067

    // No system notifications here: the window should show it. UI thread.
    public event Action<string, string, bool>? ShowInWindowRequested;

    public void Show(string title, string message, bool warning = false)
    {
        _logger.LogInformation("Notification: {Title} — {Message}", title, message);

#if WINDOWS
        if (_toasts != null && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            try
            {
                _toasts.Show(title, message);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The notification could not be shown");
            }
        }
#endif

        if (OperatingSystem.IsMacOS() && TryShowOnMac(title, message))
            return;

        Dispatcher.UIThread.Post(() => ShowInWindowRequested?.Invoke(title, message, warning));
    }

    // "display notification" posts it as Script Editor's, which macOS allows
    // without the app being notarized or asking for permission first.
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private bool TryShowOnMac(string title, string message)
    {
        try
        {
            var script = $"display notification \"{MacElevatedLauncher.EscapeForAppleScript(message)}\" "
                + $"with title \"{MacElevatedLauncher.EscapeForAppleScript(title)}\"";

            Process.Start(new ProcessStartInfo("/usr/bin/osascript", ["-e", script]) { UseShellExecute = false })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The notification could not be shown");
            return false;
        }
    }
}

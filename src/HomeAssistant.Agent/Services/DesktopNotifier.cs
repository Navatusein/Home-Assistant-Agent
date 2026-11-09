using Avalonia.Threading;
using HomeAssistant.Agent.Core.Platform;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Services;

// Notifications from the desktop agent: a system toast on Windows. Elsewhere
// (macOS for now) there is no system channel yet, so the message goes to the
// window if it is open, and to the log either way.
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

    // The user clicked a notification. Raised on the UI thread.
    public event Action? Clicked;

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

        Dispatcher.UIThread.Post(() => ShowInWindowRequested?.Invoke(title, message, warning));
    }
}

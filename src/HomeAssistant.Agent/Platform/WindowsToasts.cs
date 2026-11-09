#if WINDOWS
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace HomeAssistant.Agent.Platform;

// Windows notifications (toasts) for an app that is not packaged.
//
// A toast needs an application identity (AUMID). An unpackaged app gets one by
// registering it under HKCU\Software\Classes\AppUserModelId with the name and
// icon the notification shows; nothing else is required to display them.
// Clicking a toast reaches Activated only while this process runs, which is
// always the case here: the agent lives in the tray.
[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class WindowsToasts
{
    private const string AppId = "Navatusein.HomeAssistantAgent";
    private const string DisplayName = "Home Assistant Agent";

    private readonly ToastNotifier _notifier;

    public WindowsToasts()
    {
        Register();
        _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
    }

    public event Action? Activated;

    public void Show(string title, string message)
    {
        var xml = new XmlDocument();
        xml.LoadXml(
            "<toast><visual><binding template=\"ToastGeneric\">"
            + $"<text>{SecurityElement.Escape(title)}</text>"
            + $"<text>{SecurityElement.Escape(message)}</text>"
            + "</binding></visual></toast>");

        var toast = new ToastNotification(xml);
        toast.Activated += (_, _) => Activated?.Invoke();

        _notifier.Show(toast);
    }

    // Rewritten on every start, so a moved install still shows its icon.
    private static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");

        key.SetValue("DisplayName", DisplayName);

        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");

        if (File.Exists(icon))
            key.SetValue("IconUri", icon);
    }
}
#endif

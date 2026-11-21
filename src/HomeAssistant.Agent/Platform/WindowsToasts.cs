#if WINDOWS
using System.Runtime.Versioning;
using System.Security;
using System.Text;
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

    // The arguments a click brings back: the toast's own, or a button's.
    private const string BodyArgument = "body";
    private const string ActionArgument = "action:";

    private readonly ToastNotifier _notifier;

    public WindowsToasts()
    {
        Register();
        _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
    }

    // A toast without a click action of its own was clicked.
    public event Action? Activated;

    // image, icon: local files (a toast of an unpackaged app shows no other)
    // — a picture under the text, a square one in place of the app's icon;
    // actions: buttons, at most five.
    public void Show(string title, string message, Action? clicked = null, string? image = null,
        IReadOnlyList<(string Label, Action Clicked)>? actions = null, string? icon = null)
    {
        actions ??= [];

        var xml = new StringBuilder($"<toast launch=\"{BodyArgument}\"><visual><binding template=\"ToastGeneric\">")
            .Append($"<text>{SecurityElement.Escape(title)}</text>")
            .Append($"<text>{SecurityElement.Escape(message)}</text>");

        if (icon != null)
            xml.Append($"<image placement=\"appLogoOverride\" src=\"{SecurityElement.Escape(new Uri(icon).AbsoluteUri)}\"/>");

        if (image != null)
            xml.Append($"<image src=\"{SecurityElement.Escape(new Uri(image).AbsoluteUri)}\"/>");

        xml.Append("</binding></visual>");

        if (actions.Count > 0)
        {
            xml.Append("<actions>");

            for (var i = 0; i < actions.Count; i++)
            {
                xml.Append($"<action content=\"{SecurityElement.Escape(actions[i].Label)}\" "
                    + $"arguments=\"{ActionArgument}{i}\" activationType=\"foreground\"/>");
            }

            xml.Append("</actions>");
        }

        xml.Append("</toast>");

        var document = new XmlDocument();
        document.LoadXml(xml.ToString());

        var toast = new ToastNotification(document);

        toast.Activated += (_, args) =>
        {
            var argument = (args as ToastActivatedEventArgs)?.Arguments ?? BodyArgument;

            if (argument.StartsWith(ActionArgument, StringComparison.Ordinal)
                && int.TryParse(argument.AsSpan(ActionArgument.Length), out var index)
                && index >= 0 && index < actions.Count)
            {
                actions[index].Clicked();
                return;
            }

            (clicked ?? Activated)?.Invoke();
        };

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

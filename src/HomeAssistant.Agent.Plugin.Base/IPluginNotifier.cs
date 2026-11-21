namespace HomeAssistant.Agent.Plugin.Base;

// A system notification from a plugin: a toast on Windows, a banner on macOS.
// The desktop app shows it; the service has nobody to show it to and only
// logs it. Callable from any thread.
public interface IPluginNotifier
{
    void Show(PluginNotification notification);
}

public sealed record PluginNotification(string Title, string Message)
{
    // Opened when the notification is clicked (Windows). Only http and https:
    // anything else is dropped, so a message from outside cannot start a
    // program.
    public Uri? Link { get; init; }

    // Wants the user now: a failure, a code to type in.
    public bool Important { get; init; }

    // A picture shown in the notification (Windows; API 2.3): an http or
    // https address the agent downloads first. One it cannot get — too
    // large, not an image, unreachable — leaves the notification without it.
    public Uri? Image { get; init; }

    // A small square picture in place of the agent's icon (Windows; API 2.3):
    // the sender's logo. Fetched as Image is.
    public Uri? Icon { get; init; }

    // Buttons (Windows; API 2.3): the first five are shown, as many as a
    // toast holds. macOS shows the notification without them.
    public IReadOnlyList<PluginNotificationAction> Actions { get; init; } = [];
}

// A button of a notification: opens Link (http and https only, as the
// notification's own), or runs Clicked — the plugin's own code, on a thread
// of the agent's; an exception it throws is logged.
public sealed record PluginNotificationAction(string Label)
{
    public Uri? Link { get; init; }

    public Func<Task>? Clicked { get; init; }
}

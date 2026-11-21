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
}

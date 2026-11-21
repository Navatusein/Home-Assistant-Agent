namespace HomeAssistant.Agent.Core.Platform;

// A system notification from the desktop agent. Callable from any thread.
public interface IUserNotifier
{
    // link: opened when the notification is clicked, where the system lets
    // the agent hear about the click (Windows).
    void Show(string title, string message, bool warning = false, Uri? link = null);
}

namespace HomeAssistant.Agent.Core.Platform;

// A system notification from the desktop agent. Callable from any thread.
public interface IUserNotifier
{
    void Show(string title, string message, bool warning = false);
}

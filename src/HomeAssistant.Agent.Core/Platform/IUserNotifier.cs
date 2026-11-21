namespace HomeAssistant.Agent.Core.Platform;

// A system notification from the desktop agent. Callable from any thread.
public interface IUserNotifier
{
    // Whether a notification can carry a picture and buttons here (Windows
    // toasts). Elsewhere they are left out — and a picture not downloaded.
    bool ShowsImagesAndActions => false;

    void Show(UserNotification notification);

    // link: opened when the notification is clicked, where the system lets
    // the agent hear about the click (Windows).
    void Show(string title, string message, bool warning = false, Uri? link = null) =>
        Show(new UserNotification(title, message) { Warning = warning, Link = link });
}

public sealed record UserNotification(string Title, string Message)
{
    public bool Warning { get; init; }

    // Opened when the notification itself is clicked.
    public Uri? Link { get; init; }

    // A picture, as a local file: a toast of an app that is not packaged
    // shows no other.
    public string? ImageFile { get; init; }

    // Shown in place of the agent's icon, as a local file too.
    public string? IconFile { get; init; }

    public IReadOnlyList<UserNotificationAction> Actions { get; init; } = [];
}

// A button: opens Link, or runs Clicked.
public sealed record UserNotificationAction(string Label)
{
    public Uri? Link { get; init; }

    public Action? Clicked { get; init; }
}

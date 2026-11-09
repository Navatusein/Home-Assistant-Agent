using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using HomeAssistant.Agent.ViewModels;
using Ursa.Controls;
using Notification = Ursa.Controls.Notification;
using WindowNotificationManager = Ursa.Controls.WindowNotificationManager;

namespace HomeAssistant.Agent.Views;

public partial class MainWindow : UrsaWindow
{
    private static readonly TimeSpan NotificationDuration = TimeSpan.FromSeconds(6);

    private WindowNotificationManager? _notifications;

    public MainWindow() => InitializeComponent();

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _notifications ??= new WindowNotificationManager(this) { Position = NotificationPosition.BottomRight };
    }

    // Closing only hides the window — the application lives in the tray and
    // is shut down from there.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason is WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            Hide();

            (DataContext as MainWindowViewModel)?.OnWindowHidden();
        }

        base.OnClosing(e);
    }

    // Where there is no system notification to use. Dropped while hidden.
    public void ShowNotification(string title, string message, bool warning)
    {
        if (IsVisible)
            _notifications?.Show(new Notification(title, message,
                warning ? NotificationType.Warning : NotificationType.Information, NotificationDuration));
    }
}

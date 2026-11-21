using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Plugin.Base;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeAssistant.Agent.Tests;

public class PluginNotifierTests
{
    private sealed class RecordingNotifier(bool rich = false) : IUserNotifier
    {
        public List<UserNotification> Shown { get; } = [];

        public bool ShowsImagesAndActions => rich;

        public void Show(UserNotification notification) => Shown.Add(notification);
    }

    private static PluginNotifier Create(IUserNotifier? host, Func<Uri, CancellationToken, Task<string?>>? download = null) =>
        new(host, NullLogger.Instance, download ?? ((_, _) => Task.FromResult<string?>(null)));

    [Fact]
    public void Passes_the_notification_to_the_host()
    {
        var host = new RecordingNotifier();

        Create(host).Show(new PluginNotification("Pairing", "Enter 123 456")
        {
            Important = true,
            Link = new Uri("https://ntfy.sh/alerts"),
        });

        var shown = Assert.Single(host.Shown);
        Assert.Equal(("Pairing", "Enter 123 456", true, new Uri("https://ntfy.sh/alerts")),
            (shown.Title, shown.Message, shown.Warning, shown.Link));
    }

    // A link from outside must not run anything when the toast is clicked.
    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    public void Drops_links_that_are_not_web_pages(string link)
    {
        var host = new RecordingNotifier(rich: true);

        Create(host).Show(new PluginNotification("t", "m")
        {
            Link = new Uri(link),
            Actions = [new PluginNotificationAction("Open") { Link = new Uri(link) }],
        });

        var shown = Assert.Single(host.Shown);
        Assert.Null(shown.Link);
        Assert.Empty(shown.Actions);
    }

    [Fact]
    public void Without_a_host_notifier_only_logs()
    {
        Create(null).Show(new PluginNotification("t", "m"));
    }

    // Windows: up to five buttons; one that does nothing is left out.
    [Fact]
    public void Passes_up_to_five_buttons()
    {
        var host = new RecordingNotifier(rich: true);
        var actions = Enumerable.Range(1, 7)
            .Select(i => new PluginNotificationAction($"Button {i}") { Link = new Uri($"https://x/{i}") })
            .Prepend(new PluginNotificationAction("Nothing"))
            .ToArray();

        Create(host).Show(new PluginNotification("t", "m") { Actions = actions });

        Assert.Equal(["Button 1", "Button 2", "Button 3", "Button 4", "Button 5"],
            Assert.Single(host.Shown).Actions.Select(action => action.Label));
    }

    // The plugin's code runs on the click, and a failure stays in the log.
    [Fact]
    public async Task A_button_runs_the_plugins_code()
    {
        var host = new RecordingNotifier(rich: true);
        var clicked = new TaskCompletionSource();

        Create(host).Show(new PluginNotification("t", "m")
        {
            Actions =
            [
                new PluginNotificationAction("Run") { Clicked = () => { clicked.SetResult(); return Task.CompletedTask; } },
                new PluginNotificationAction("Fail") { Clicked = () => throw new InvalidOperationException("boom") },
            ],
        });

        var actions = Assert.Single(host.Shown).Actions;

        actions[0].Clicked!();
        actions[1].Clicked!();

        await clicked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Shows_the_pictures_it_fetched()
    {
        var shown = new TaskCompletionSource<UserNotification>();
        var notifier = new PluginNotifier(new CallbackNotifier(shown), NullLogger.Instance,
            (picture, _) => Task.FromResult<string?>("fetched" + picture.AbsolutePath));

        notifier.Show(new PluginNotification("t", "m")
        {
            Image = new Uri("https://x/cat.png"),
            Icon = new Uri("https://x/logo.png"),
        });

        var notification = await shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(("fetched/cat.png", "fetched/logo.png"), (notification.ImageFile, notification.IconFile));
    }

    // A picture that cannot be fetched leaves the notification without it.
    [Fact]
    public async Task Shows_the_notification_without_a_picture_it_could_not_fetch()
    {
        var shown = new TaskCompletionSource<UserNotification>();
        var notifier = new PluginNotifier(new CallbackNotifier(shown), NullLogger.Instance,
            (_, _) => throw new HttpRequestException("404"));

        notifier.Show(new PluginNotification("t", "m") { Image = new Uri("https://x/missing.png") });

        Assert.Null((await shown.Task.WaitAsync(TimeSpan.FromSeconds(5))).ImageFile);
    }

    // Where a notification shows no picture (macOS), none is downloaded.
    [Fact]
    public void Fetches_no_picture_for_a_host_that_shows_none()
    {
        var host = new RecordingNotifier();
        var fetched = false;

        Create(host, (_, _) => { fetched = true; return Task.FromResult<string?>(null); })
            .Show(new PluginNotification("t", "m")
            {
                Image = new Uri("https://x/cat.png"),
                Actions = [new PluginNotificationAction("Open") { Link = new Uri("https://x") }],
            });

        var shown = Assert.Single(host.Shown);
        Assert.False(fetched);
        Assert.Null(shown.ImageFile);
        Assert.Empty(shown.Actions);
    }

    private sealed class CallbackNotifier(TaskCompletionSource<UserNotification> shown) : IUserNotifier
    {
        public bool ShowsImagesAndActions => true;

        public void Show(UserNotification notification) => shown.TrySetResult(notification);
    }
}

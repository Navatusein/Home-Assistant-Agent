using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Plugin.Base;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeAssistant.Agent.Tests;

public class PluginNotifierTests
{
    private sealed class RecordingNotifier : IUserNotifier
    {
        public List<(string Title, string Message, bool Warning, Uri? Link)> Shown { get; } = [];

        public void Show(string title, string message, bool warning = false, Uri? link = null) =>
            Shown.Add((title, message, warning, link));
    }

    [Fact]
    public void Passes_the_notification_to_the_host()
    {
        var host = new RecordingNotifier();

        new PluginNotifier(host, NullLogger.Instance).Show(new PluginNotification("Pairing", "Enter 123 456")
        {
            Important = true,
            Link = new Uri("https://ntfy.sh/alerts"),
        });

        Assert.Equal([("Pairing", "Enter 123 456", true, new Uri("https://ntfy.sh/alerts"))], host.Shown);
    }

    // A link from outside must not run anything when the toast is clicked.
    [Theory]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("ms-settings:privacy")]
    [InlineData("javascript:alert(1)")]
    public void Drops_links_that_are_not_web_pages(string link)
    {
        var host = new RecordingNotifier();

        new PluginNotifier(host, NullLogger.Instance).Show(new PluginNotification("t", "m") { Link = new Uri(link) });

        Assert.Null(Assert.Single(host.Shown).Link);
    }

    [Fact]
    public void Without_a_host_notifier_only_logs()
    {
        new PluginNotifier(null, NullLogger.Instance).Show(new PluginNotification("t", "m"));
    }
}

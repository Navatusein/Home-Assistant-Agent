using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Plugin.Base;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Core.Plugins;

// What a plugin's IPluginNotifier is: the host's notifier, or the log where
// there is none (the service). The log line carries the plugin, as any line
// the plugin writes.
internal sealed class PluginNotifier(IUserNotifier? notifier, ILogger logger) : IPluginNotifier
{
    public void Show(PluginNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var link = notification.Link;

        // A click opens the link with the shell: anything but a web page could
        // start a program, and the text may come from outside (ntfy, MQTT).
        if (link != null && !(link.IsAbsoluteUri && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)))
        {
            logger.LogWarning("Notification link {Link} dropped: only http and https open", link);
            link = null;
        }

        if (notifier == null)
        {
            logger.LogInformation("Notification: {Title} — {Message}", notification.Title, notification.Message);
            return;
        }

        notifier.Show(notification.Title, notification.Message, notification.Important, link);
    }
}

using System.Net.Http.Headers;
using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Plugin.Base;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Core.Plugins;

// What a plugin's IPluginNotifier is: the host's notifier, or the log where
// there is none (the service). The log line carries the plugin, as any line
// the plugin writes.
//
// The text of a notification may come from outside (ntfy, MQTT), so links
// open only as web pages and a picture is fetched with limits, before the
// notification is shown — off the caller's thread then.
internal sealed class PluginNotifier : IPluginNotifier
{
    // As many buttons as a Windows toast holds.
    internal const int MaxActions = 5;

    private const long MaxImageBytes = 5 * 1024 * 1024;
    private static readonly TimeSpan ImageTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ImageLifetime = TimeSpan.FromHours(1);
    private static readonly HttpClient Http = new() { Timeout = ImageTimeout };

    private readonly IUserNotifier? _notifier;
    private readonly ILogger _logger;
    private readonly Func<Uri, CancellationToken, Task<string?>> _downloadImage;

    // downloadImage: a test's stand-in for the download; the file it returns
    // is shown, null shows none.
    public PluginNotifier(IUserNotifier? notifier, ILogger logger,
        Func<Uri, CancellationToken, Task<string?>>? downloadImage = null)
    {
        _notifier = notifier;
        _logger = logger;
        _downloadImage = downloadImage ?? DownloadImageAsync;
    }

    public void Show(PluginNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (_notifier == null)
        {
            _logger.LogInformation("Notification: {Title} — {Message}", notification.Title, notification.Message);
            return;
        }

        var shown = new UserNotification(notification.Title, notification.Message)
        {
            Warning = notification.Important,
            Link = WebLink(notification.Link),
        };

        if (!_notifier.ShowsImagesAndActions)
        {
            _notifier.Show(shown);
            return;
        }

        shown = shown with
        {
            Actions = notification.Actions
                .Select(ToUserAction)
                .OfType<UserNotificationAction>()
                .Take(MaxActions)
                .ToArray()
        };

        var image = WebLink(notification.Image);
        var icon = WebLink(notification.Icon);

        if (image == null && icon == null)
        {
            _notifier.Show(shown);
            return;
        }

        _ = ShowWithPicturesAsync(shown, image, icon);
    }

    // Both fetched at once; one that fails is left out, the notification is
    // shown either way.
    private async Task ShowWithPicturesAsync(UserNotification notification, Uri? image, Uri? icon)
    {
        var files = await Task.WhenAll(FetchAsync(image), FetchAsync(icon));

        _notifier!.Show(notification with { ImageFile = files[0], IconFile = files[1] });
    }

    private async Task<string?> FetchAsync(Uri? picture)
    {
        if (picture == null)
            return null;

        try
        {
            return await _downloadImage(picture, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("The picture of a notification could not be fetched from {Image}: {Error}", picture, ex.Message);
            return null;
        }
    }

    // A button needs something to do: a web link, or the plugin's code.
    private UserNotificationAction? ToUserAction(PluginNotificationAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Label))
            return null;

        var link = WebLink(action.Link);

        if (link == null && action.Clicked == null)
            return null;

        return new UserNotificationAction(action.Label)
        {
            Link = link,
            Clicked = action.Clicked == null ? null : () => _ = RunAsync(action),
        };
    }

    private async Task RunAsync(PluginNotificationAction action)
    {
        try
        {
            await action.Clicked!();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The notification button {Label} failed", action.Label);
        }
    }

    // A click opens the link with the shell: anything but a web page could
    // start a program.
    private Uri? WebLink(Uri? link)
    {
        if (link == null || link.IsAbsoluteUri && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp))
            return link;

        _logger.LogWarning("Notification link {Link} dropped: only http and https open", link);
        return null;
    }

    private static string ImageFolder => Path.Combine(Path.GetTempPath(), AgentPaths.DataFolderName, "notification-images");

    // Into a temporary file the toast reads; files left from earlier
    // notifications go once they are an hour old.
    private static async Task<string?> DownloadImageAsync(Uri image, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ImageFolder);
        DeleteOldImages();

        using var response = await Http.GetAsync(image, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"not an image ({(type.Length > 0 ? type : "no type")})");

        if (response.Content.Headers.ContentLength > MaxImageBytes)
            throw new InvalidOperationException("larger than 5 MB");

        var file = Path.Combine(ImageFolder, Guid.NewGuid().ToString("N") + Extension(response.Content.Headers.ContentType));

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(file))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;

                if (total > MaxImageBytes)
                {
                    target.Close();
                    File.Delete(file);
                    throw new InvalidOperationException("larger than 5 MB");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }

        return file;
    }

    private static string Extension(MediaTypeHeaderValue? type) => type?.MediaType?.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => ".jpg",
    };

    private static void DeleteOldImages()
    {
        foreach (var file in Directory.EnumerateFiles(ImageFolder))
        {
            try
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > ImageLifetime)
                    File.Delete(file);
            }
            catch (IOException)
            {
                // Still shown; the next round takes it.
            }
        }
    }
}

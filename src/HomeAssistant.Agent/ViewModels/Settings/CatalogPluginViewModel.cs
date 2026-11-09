using Avalonia.Media.Imaging;
using HomeAssistant.Agent.Core.Updates;

namespace HomeAssistant.Agent.ViewModels.Settings;

// A plugin a catalog offers that is not installed yet.
public sealed partial class CatalogPluginViewModel : ObservableObject
{
    private static readonly HttpClient IconClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public CatalogPluginViewModel(PluginOffer offer)
    {
        Offer = offer;

        if (Icon != null)
            _ = LoadIconAsync(Icon);
    }

    public PluginOffer Offer { get; }

    // The catalog's icon once downloaded; until then (or without one) the
    // card shows the generic puzzle piece.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private Bitmap? _iconImage;

    private async Task LoadIconAsync(Uri uri)
    {
        try
        {
            await using var stream = new MemoryStream(await IconClient.GetByteArrayAsync(uri));
            IconImage = Bitmap.DecodeToWidth(stream, 64);
        }
        catch (Exception)
        {
            // A missing or broken icon only costs the picture.
        }
    }

    public string Name => Offer.Plugin.Name;

    public string? Description => Offer.Plugin.Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    // "1.2.0 · Author · github.com/owner/repo"
    public string Byline => string.Join(" · ",
        new[] { Offer.Plugin.Version, Offer.Plugin.Author, Offer.Source.DisplayName }.Where(part => !string.IsNullOrWhiteSpace(part)));

    public Uri? Icon => Uri.TryCreate(Offer.Plugin.Icon, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        ? uri
        : null;

    public bool HasIcon => IconImage != null;

    public bool CanInstall => Offer.State == PluginOfferState.Available;

    public string? Note => Offer.State == PluginOfferState.NeedsNewerAgent
        ? $"Needs a newer agent (plugin API {Offer.Plugin.ApiVersion})."
        : null;

    public bool HasNote => Note != null;

    public Uri? Link => Uri.TryCreate(Offer.Plugin.Repository ?? Offer.Plugin.Homepage, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;

    public bool HasLink => Link != null;
}

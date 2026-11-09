using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace HomeAssistant.Agent.Services;

// The tray icon is the application's real entry point: the window starts
// hidden and is only shown from here. On Windows it is the notification area
// icon, on macOS the menu bar item.
//
// Built in code rather than declared in App.axaml so the host decides when it
// appears (after the agent state is loaded) and can change the menu.
public sealed class TrayIconService : IDisposable
{
    // The menu icons. Drawn at exactly the slot's size: the tray menu takes a
    // bitmap's pixels as its size, so a 2x one shows only its top-left corner.
    private const int IconSize = 16;

    private TrayIcon? _trayIcon;

    // Every item with the Semi icon it shows; redrawn when the theme changes.
    private readonly List<(NativeMenuItem Item, string IconKey)> _iconItems = [];

    // Shown at the top of the menu only while an agent update is available.
    private readonly NativeMenuItem _updateItem;
    private readonly NativeMenuItemSeparator _updateSeparator = new() { IsVisible = false };

    public TrayIconService()
    {
        _updateItem = NewItem("Update", "SemiIconDownload", () => UpdateRequested?.Invoke());
        _updateItem.IsVisible = false;
    }

    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? RestartRequested;
    public event Action? ExitRequested;
    public event Action? UpdateRequested;

    // null hides the entry. UI thread only.
    public void ShowUpdate(string? version)
    {
        _updateItem.Header = $"Update to {version}";
        _updateItem.IsVisible = version != null;
        _updateSeparator.IsVisible = version != null;
    }

    // The same orderly exit as the menu's Exit, for code that needs the app gone
    // (the plugin installer replaces files the app holds open).
    public void RequestExit() => ExitRequested?.Invoke();

    public void Show()
    {
        if (_trayIcon != null || Application.Current is not { } application)
            return;

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Home Assistant Agent",
            Icon = LoadTrayIcon(),
            Menu =
            [
                _updateItem,
                _updateSeparator,
                NewItem("Open", "SemiIconHome", () => OpenRequested?.Invoke()),
                NewItem("Settings", "SemiIconSetting", () => SettingsRequested?.Invoke()),
                new NativeMenuItemSeparator(),
                NewItem("Restart", "SemiIconRefresh", () => RestartRequested?.Invoke()),
                NewItem("Exit", "SemiIconExit", () => ExitRequested?.Invoke()),
            ],
        };

        // A left click on Windows; on macOS the click opens the menu instead.
        _trayIcon.Clicked += (_, _) => OpenRequested?.Invoke();

        // The icons are drawn in the theme's text colour.
        DrawMenuIcons(application);
        application.ActualThemeVariantChanged += (_, _) => DrawMenuIcons(application);

        TrayIcon.SetIcons(application, [_trayIcon]);
    }

    public void Dispose()
    {
        if (_trayIcon == null)
            return;

        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private NativeMenuItem NewItem(string header, string iconKey, Action onClick)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => onClick();

        _iconItems.Add((item, iconKey));
        return item;
    }

    private void DrawMenuIcons(Application application)
    {
        var brush = new SolidColorBrush(application.ActualThemeVariant == ThemeVariant.Dark
            ? Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0xCC, 0x00, 0x00, 0x00));

        foreach (var (item, key) in _iconItems)
        {
            var old = item.Icon;
            item.Icon = application.TryFindResource(key, out var resource) && resource is Geometry geometry
                ? DrawIcon(geometry, brush)
                : null;
            old?.Dispose();
        }
    }

    // A Semi icon is a vector path; the menu takes a bitmap. Fitted into the
    // square with its aspect ratio kept.
    private static Bitmap DrawIcon(Geometry geometry, IBrush brush)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(IconSize, IconSize), new Vector(96, 96));

        var bounds = geometry.Bounds;
        var scale = IconSize / Math.Max(bounds.Width, bounds.Height);
        var offsetX = (IconSize - bounds.Width * scale) / 2;
        var offsetY = (IconSize - bounds.Height * scale) / 2;

        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(
                   Matrix.CreateTranslation(-bounds.X, -bounds.Y)
                   * Matrix.CreateScale(scale, scale)
                   * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            context.DrawGeometry(brush, null, geometry);
        }

        return bitmap;
    }

    private static WindowIcon? LoadTrayIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://HomeAssistant.Agent/Assets/icon.ico"));
            return new WindowIcon(stream);
        }
        catch (Exception)
        {
            // Without an icon the tray entry is invisible but the menu still
            // works — not worth failing startup over.
            return null;
        }
    }
}

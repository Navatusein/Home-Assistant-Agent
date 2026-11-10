using Avalonia.Media.Imaging;
using HomeAssistant.Agent.Core;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Core.Settings;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.ViewModels.Settings.Controls;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.ViewModels.Settings;

// A settings section built from a plugin's ConfigDescriptor. Unlike General,
// nothing here is hand-written: the plugin declares its controls and the host
// renders whatever it gets.
public partial class PluginSettingsSectionViewModel : ObservableObject, ISettingsSection
{
    private readonly PluginToolOptionsProvider _options;

    public PluginSettingsSectionViewModel(BasePlugin plugin, ILogger logger)
    {
        Name = plugin.PluginCode;
        Title = plugin.PluginName;

        var info = plugin.Info;

        Description = info.Description;
        Author = info.Author;
        Version = info.Version;
        Homepage = info.Homepage;
        Repository = info.Repository;
        PluginIcon = LoadIcon(info, plugin.PluginCode, logger);
        PluginFileName = FindRemovableFile(plugin);

        _options = new PluginToolOptionsProvider(plugin, logger);

        Controls = plugin.ConfigDescriptor.Controls
            .Select(control => SettingViewModelFactory.Create(control, _options))
            .ToArray();

        Features = plugin.Features
            .Select(feature => new FeatureSettingViewModel(feature, _options))
            .ToArray();
    }

    public string Name { get; }

    public string Title { get; }

    // From the plugin's project file (PluginInfo). Any of them may be missing.
    public string? Description { get; }

    public string? Author { get; }

    public string? Version { get; }

    public Uri? Homepage { get; }

    public Uri? Repository { get; }

    // The plugin's own icon; null shows the generic puzzle piece instead.
    public Bitmap? PluginIcon { get; }

    public bool HasPluginIcon => PluginIcon != null;

    public bool HasDescription => Description != null;

    public bool HasHomepage => Homepage != null;

    public bool HasRepository => Repository != null;

    // "1.2.0 · Navatusein" — whichever parts the plugin declares.
    public string Byline => string.Join(" · ", new[] { Version, Author }.Where(part => part != null));

    private static Bitmap? LoadIcon(PluginInfo info, string pluginCode, ILogger logger)
    {
        try
        {
            using var stream = info.OpenIcon();

            if (stream == null)
                return null;

            return Bitmap.DecodeToWidth(stream, 64);
        }
        catch (Exception ex)
        {
            // A broken icon is the plugin author's problem, not a reason to
            // lose the settings page.
            logger.LogWarning(ex, "The icon of {PluginCode} could not be loaded", pluginCode);
            return null;
        }
    }

    // Plugin fields carry no validation rules — the plugin decides for itself
    // whether the values are usable, in CreateConfig.
    public bool HasErrors => false;

    public IReadOnlyList<SettingControlViewModel> Controls { get; }

    // The plugin-wide controls as they are drawn: one card per run of
    // controls, one per group.
    public IReadOnlyList<SettingBlock> Blocks => _blocks ??= SettingBlock.From(Controls);

    private IReadOnlyList<SettingBlock>? _blocks;

    public bool HasBlocks => Controls.Count > 0;

    // Plugins without controls still get a card for the switch, just no gear.
    // Whether the plugin's page has anything to show beyond the About block:
    // plugin-wide settings or features to switch.
    public bool HasControls => Controls.Count > 0 || Features.Count > 0;

    public IReadOnlyList<FeatureSettingViewModel> Features { get; }

    public bool HasFeatures => Features.Count > 0;

    // Every setting control on the page — the plugin's own and its features'.
    private IEnumerable<SettingControlViewModel> AllControls =>
        Controls.Concat(Features.SelectMany(feature => feature.Controls));

    // The switch on the plugin's card. Stored alongside the plugin's own
    // values and applied on Save, like every other setting.
    [ObservableProperty]
    private bool _isPluginEnabled = true;

    // A newer version in the catalog this plugin came from; set after each
    // update check.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(UpdateText))]
    private Core.Updates.PluginOffer? _update;

    public bool HasUpdate => Update != null;

    public string UpdateText => $"Update to {Update?.Plugin.Version}";

    // The dll the plugin was loaded from, when it sits right in the installed
    // plugins folder — whether it came from a catalog, an older installer or
    // was copied in by hand. Null for a plugin that cannot be removed here: one
    // in a folder of its own, or one inside the app bundle on macOS.
    public string? PluginFileName { get; }

    public bool CanRemove => PluginFileName != null;

    private static string? FindRemovableFile(BasePlugin plugin)
    {
        var location = plugin.GetType().Assembly.Location;

        if (string.IsNullOrEmpty(location))
            return null;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(location)!));
        var installed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AgentPaths.InstalledPluginsDirectory));

        return string.Equals(directory, installed, comparison) ? Path.GetFileName(location) : null;
    }

    public string PluginCode => Name;

    // "Default" follows the general log level; anything else overrides it for
    // this plugin only — the way to turn on Debug for the one plugin being
    // chased without flooding the log with everything else.
    public const string DefaultLogLevel = "Default";

    public IReadOnlyList<string> LogLevelOptions { get; } = [DefaultLogLevel, .. LogLevels.Names];

    [ObservableProperty]
    private string _logLevel = DefaultLogLevel;

    public void Load(AgentSettings settings)
    {
        var plugin = settings.GetPlugin(PluginCode);

        IsPluginEnabled = plugin.Enabled;

        LogLevel = plugin.GetLogLevel() is { } level
            ? LogLevels.ToName(level)
            : DefaultLogLevel;

        SettingValueCollector.Apply(Controls, plugin.Config);

        foreach (var feature in Features)
            feature.Load(plugin);
    }

    public void Save(AgentSettings settings)
    {
        var plugin = new PluginSettings
        {
            Enabled = IsPluginEnabled,

            // Nothing stored for "Default", so the plugin keeps following the
            // general level when that changes.
            LogLevel = LogLevel != DefaultLogLevel ? LogLevel : null,
        };

        SettingValueCollector.Collect(Controls, plugin.Config);

        foreach (var feature in Features)
            feature.Save(plugin);

        settings.Plugins[PluginCode] = plugin;
    }

    // Asking the plugin for its options is deferred until the section is opened
    // — enumerating audio endpoints for a plugin the user never looks at would
    // be work done for nothing.
    public void OnActivated() => _ = SettingValueCollector.LoadOptionsAsync(AllControls);

    public void ResetViewState()
    {
        foreach (var control in AllControls)
            control.ResetViewState();
    }
}
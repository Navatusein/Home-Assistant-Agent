using HomeAssistant.Agent.Core.Settings;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.ViewModels.Settings.Controls;

namespace HomeAssistant.Agent.ViewModels.Settings;

// One feature on a plugin's settings page: its switch, which host runs it,
// and the settings that belong to it. The switch is stored in the plugin's
// "features", its settings in the plugin's "config", and both are applied on
// Save like every other setting.
public partial class FeatureSettingViewModel : ObservableObject
{
    private readonly PluginFeature _feature;

    public FeatureSettingViewModel(PluginFeature feature, PluginToolOptionsProvider options)
    {
        _feature = feature;

        Controls = feature.Controls
            .Select(control => SettingViewModelFactory.Create(control, options))
            .ToArray();

        _isEnabled = feature.EnabledByDefault;
    }

    public string Name => _feature.Name;

    public string Description => _feature.Description;

    // The service runs without a desktop, so its features are worth calling
    // out: they work while nobody is signed in.
    public bool IsServiceFeature => _feature.Host == PluginHostMode.Service;

    public string HostLabel => IsServiceFeature ? "Service" : "App";

    public string HostToolTip => IsServiceFeature
        ? "Runs in the Windows service, also before sign-in."
        : "Runs in the desktop app while you are signed in.";

    public IReadOnlyList<SettingControlViewModel> Controls { get; }

    public IReadOnlyList<SettingBlock> Blocks => _blocks ??= SettingBlock.From(Controls);

    private IReadOnlyList<SettingBlock>? _blocks;

    public bool HasControls => Controls.Count > 0;

    // What the card shows under its row: the feature itself when it has
    // settings, nothing otherwise (no chevron, no expanding).
    public FeatureSettingViewModel? Detail => HasControls ? this : null;

    // A switched-off feature keeps its settings, inactive: switching it back
    // on brings them back as they were.
    [ObservableProperty]
    private bool _isEnabled;

    public void Load(PluginSettings settings)
    {
        IsEnabled = settings.IsFeatureEnabled(_feature);
        SettingValueCollector.Apply(Controls, settings.Config);
    }

    public void Save(PluginSettings settings)
    {
        SettingValueCollector.Collect(Controls, settings.Config);
        settings.Features[_feature.Code] = IsEnabled;
    }
}

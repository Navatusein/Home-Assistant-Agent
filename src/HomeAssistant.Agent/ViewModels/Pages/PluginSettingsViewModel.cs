namespace HomeAssistant.Agent.ViewModels.Pages;

// The per-plugin settings page reached from a card on the Settings page. It has
// no state of its own: saving, cancelling and the section being shown all live
// on the shared SettingsViewModel, so a plugin's edits are persisted by the same
// Save that persists the application's own settings.
//
// It deliberately does not implement INavigationAware — the plugin section is
// activated when its card is opened (SettingsViewModel.OpenPlugin), and keeping
// this page's view model out of the navigation lifecycle leaves the General
// section's polling tied solely to the Settings page it belongs to.
public class PluginSettingsViewModel(SettingsViewModel settings)
{
    public SettingsViewModel Settings { get; } = settings;

    // The plugin on screen. Read when the page is built, which happens on
    // every visit: the section is chosen before navigating here.
    public Settings.PluginSettingsSectionViewModel? Section =>
        Settings.SelectedSection as Settings.PluginSettingsSectionViewModel;
}
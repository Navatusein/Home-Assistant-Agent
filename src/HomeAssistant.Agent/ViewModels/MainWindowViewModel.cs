using System.ComponentModel;
using HomeAssistant.Agent.Services;
using HomeAssistant.Agent.ViewModels.Pages;

namespace HomeAssistant.Agent.ViewModels;

// The window: three tabs across the top, and the page under them. A plugin's
// own page sits under the Settings tab, with a way back to Settings.
public sealed partial class MainWindowViewModel : ObservableObject
{
    // The tabs, in order.
    private static readonly Type[] TabPages = [typeof(DashboardViewModel), typeof(LogsViewModel), typeof(SettingsViewModel)];

    private readonly SettingsViewModel _settings;

    public MainWindowViewModel(NavigationService navigation, SettingsViewModel settings)
    {
        Navigation = navigation;
        _settings = settings;

        navigation.PropertyChanged += OnNavigationChanged;
    }

    public NavigationService Navigation { get; }

    // -1 while nothing is shown (the window is hidden).
    public int SelectedTab
    {
        get => Navigation.CurrentPage switch
        {
            null => -1,
            PluginSettingsViewModel => Array.IndexOf(TabPages, typeof(SettingsViewModel)),
            var page => Array.IndexOf(TabPages, page.GetType()),
        };
        set
        {
            if (value >= 0 && value < TabPages.Length && value != SelectedTab)
                Navigation.Navigate(TabPages[value]);
        }
    }

    public bool IsPluginPage => Navigation.CurrentPage is PluginSettingsViewModel;

    // "Settings › Windows Plugin" above a plugin's page.
    public string? PluginTitle => _settings.SelectedSection?.Title;

    [RelayCommand]
    private void GoBack() => Navigation.Navigate<SettingsViewModel>();

    // Closing the window only hides it — the application lives in the tray.
    // Hiding leaves the view models alive, so the next open would resume
    // mid-session (status bar still up, cards expanded, unsaved edits).
    // Opening the window is supposed to look like a fresh start.
    public void OnWindowHidden()
    {
        Navigation.Clear();
        _settings.ResetViewState();
    }

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NavigationService.CurrentPage))
            return;

        OnPropertyChanged(nameof(SelectedTab));
        OnPropertyChanged(nameof(IsPluginPage));
        OnPropertyChanged(nameof(PluginTitle));
    }
}

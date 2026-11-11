using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Services;
using HomeAssistant.Agent.ViewModels.Settings;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.ViewModels.Pages
{
    public partial class SettingsViewModel : ObservableObject, INavigationAware
    {
        private readonly AgentService _agentService;
        private readonly NavigationService _navigation;
        private readonly UpdateService _updates;
        private readonly ILogger<SettingsViewModel> _logger;

        private bool _isLoaded;

        public SettingsViewModel(
            IEnumerable<ISettingsSection> sections,
            PluginManager pluginManager,
            AgentService agentService,
            NavigationService navigation,
            UpdateService updates,
            ILogger<SettingsViewModel> logger)
        {
            _agentService = agentService;
            _navigation = navigation;
            _updates = updates;
            _logger = logger;

            // The injected sections describe the application itself and are
            // shown, one after another, on the main Settings page.
            AppSections = sections.ToArray();

            // Plugin sections cannot be registered in DI: which plugins exist is
            // only known once they have been loaded from disk. Every plugin gets
            // a card so it can be switched off; only those with controls get a
            // settings page.
            PluginSections = pluginManager.Plugins
                .Select(plugin => new PluginSettingsSectionViewModel(plugin, logger))
                .ToArray();

            Sections = new ObservableCollection<ISettingsSection>(AppSections.Concat(PluginSections));

            AppVersion = $"Home Assistant Agent {GetAssemblyVersion()}";

            General = AppSections.OfType<GeneralSettingsViewModel>().First();

            updates.Changed += () => Dispatcher.UIThread.Post(RefreshCatalog);
            RefreshCatalog();
        }

        // The application's settings, for the parts of them shown in the plugin
        // area (catalog sources).
        public GeneralSettingsViewModel General { get; }

        // What the catalogs offer that is not installed yet.
        public ObservableCollection<CatalogPluginViewModel> AvailablePlugins { get; } = [];

        public bool HasAvailablePlugins => AvailablePlugins.Count > 0;

        public string OfficialSource => Core.Updates.CatalogClient.OfficialUrl;

        [ObservableProperty]
        private string _catalogStatus = string.Empty;

        // Maps the latest update check onto the plugin cards and the list of
        // plugins to install.
        private void RefreshCatalog()
        {
            var result = _updates.Result;
            foreach (var section in PluginSections)
            {
                var offer = result.FindPlugin(section.PluginCode);
                section.Update = offer?.State == Core.Updates.PluginOfferState.UpdateAvailable ? offer : null;
            }

            AvailablePlugins.Clear();

            foreach (var offer in result.Plugins.Where(offer => offer.InstalledVersion == null
                         && PluginSections.All(section => !string.Equals(section.PluginCode, offer.Plugin.Code, StringComparison.OrdinalIgnoreCase))))
            {
                AvailablePlugins.Add(new CatalogPluginViewModel(offer));
            }

            OnPropertyChanged(nameof(HasAvailablePlugins));

            var unreachable = result.Sources.Where(source => source.Error != null).Select(source => source.Source.DisplayName).ToArray();

            CatalogStatus = _updates.IsChecking
                ? "Checking the catalogs…"
                : result.CheckedAt == DateTimeOffset.MinValue
                    ? "The catalogs have not been checked yet."
                    : $"Checked {result.CheckedAt:g}."
                      + (unreachable.Length > 0 ? $" Unreachable: {string.Join(", ", unreachable)}." : string.Empty);
        }

        [RelayCommand]
        private async Task CheckCatalogsAsync()
        {
            try
            {
                await _updates.CheckAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Catalog check failed");
                ShowStatus($"Checking the catalogs failed: {ex.Message}", NotificationType.Warning);
            }
        }

        [RelayCommand]
        private Task InstallPluginAsync(CatalogPluginViewModel? plugin) =>
            plugin == null
                ? Task.CompletedTask
                : ChangePluginsAsync([plugin.Offer], [], plugin.Offer.Source.IsOfficial
                    ? $"Install {plugin.Name} {plugin.Offer.Plugin.Version}? The agent restarts to load it."
                    : $"Install {plugin.Name} {plugin.Offer.Plugin.Version} from {plugin.Offer.Source.DisplayName}?\n\n"
                      + "It is not from the official catalog. A plugin runs with full rights on this PC — "
                      + "also in the Windows service, as SYSTEM. Install only what you trust.\n\nThe agent restarts to load it.");

        [RelayCommand]
        private Task UpdatePluginAsync(PluginSettingsSectionViewModel? section) =>
            section?.Update is not { } offer
                ? Task.CompletedTask
                : ChangePluginsAsync([offer], [], $"Update {section.Title} to {offer.Plugin.Version}? The agent restarts to load it.");

        [RelayCommand]
        private Task RemovePluginAsync(PluginSettingsSectionViewModel? section) =>
            section?.PluginFileName is not { } fileName
                ? Task.CompletedTask
                : ChangePluginsAsync([], [new Core.Updates.PluginRemoval(section.PluginCode, fileName)],
                    $"Remove {section.Title}? Its entities disappear from Home Assistant, and the agent restarts.");

        // Plugin files are admin-only and locked while the agent runs: after
        // the confirmation, UpdateService downloads, asks for administrator
        // approval and hands over to the elevated step, and the app exits.
        private async Task ChangePluginsAsync(IReadOnlyList<Core.Updates.PluginOffer> installs,
            IReadOnlyList<Core.Updates.PluginRemoval> removals, string question)
        {
            if (!await Dialogs.ConfirmAsync("Plugins", question))
                return;

            IsBusy = true;

            try
            {
                await _updates.ChangePluginsAsync(installs, removals);
                ShowStatus("Applying — the agent restarts in a moment.", NotificationType.Information);
            }
            catch (OperationCanceledException)
            {
                ShowStatus("Changing plugins needs administrator approval.", NotificationType.Warning);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Changing plugins failed");
                ShowStatus($"Changing plugins failed: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // Every section, in menu order — the unit Save and Load iterate over.
        public ObservableCollection<ISettingsSection> Sections { get; }

        // The application's own sections, rendered inline on the Settings page.
        public IReadOnlyList<ISettingsSection> AppSections { get; }

        // Plugin sections, shown as cards that link to their own page.
        public IReadOnlyList<PluginSettingsSectionViewModel> PluginSections { get; }

        public bool HasPlugins => PluginSections.Count > 0;

        // Importing data only makes sense for an installed copy: a portable
        // one already keeps its data next to itself.
        public bool CanImportData => !Core.AgentPaths.IsPortable;

        // Copies the settings, plugin state and discovery records of an earlier
        // copy of the agent (a development build, an unzipped one) into this
        // installation, then restarts: plugin state is held in memory.
        [RelayCommand]
        private async Task ImportDataAsync()
        {
            var folder = await Dialogs.OpenFolderAsync("Folder of the previous version (the one with Settings.json)", null);

            if (folder == null)
                return;

            if (!DataImport.ContainsData(folder))
            {
                ShowStatus("That folder has no Settings.json.", NotificationType.Warning);
                return;
            }

            if (!await Dialogs.ConfirmAsync("Import settings", "The current settings and plugin pairings are replaced by the ones in that folder, "
                    + "and the agent restarts."))
                return;

            try
            {
                var copied = DataImport.Import(folder, Core.AgentPaths.DataDirectory);

                _logger.LogInformation("Imported {Files} from {Folder}", string.Join(", ", copied), folder);

                AgentRestart.Start();
                ShowStatus("Imported — restarting…", NotificationType.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Import from {Folder} failed", folder);
                ShowStatus($"Import failed: {ex.Message}", NotificationType.Error);
            }
        }

        [ObservableProperty]
        private ISettingsSection? _selectedSection;

        [ObservableProperty]
        private string _appVersion;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private NotificationType _statusSeverity = NotificationType.Information;

        [ObservableProperty]
        private bool _isStatusOpen;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotBusy))]
        private bool _isBusy;

        public bool IsNotBusy => !IsBusy;

        // Fires only for the Settings page: the per-plugin page uses its own view
        // model, so navigating into a plugin does not re-run this.
        public void OnNavigatedTo()
        {
            // Load once — arriving back from a plugin page must not throw away
            // edits the user made before they got a chance to save.
            if (!_isLoaded)
            {
                LoadFromSettings();
                _isLoaded = true;
            }

            // The application's sections are on screen now, so their live parts
            // (the Windows service poller) run while the page is shown.
            foreach (var section in AppSections)
                section.OnActivated();
        }

        public void OnNavigatedFrom()
        {
            foreach (var section in AppSections)
                section.OnDeactivated();
        }

        // Closing the window only hides it, and every page and section here is a
        // singleton, so nothing resets on its own: a reopened window would still
        // show the last visit's status bar, expanded cards and unsaved edits.
        // Called from MainWindow as it hides.
        public void ResetViewState()
        {
            IsStatusOpen = false;

            foreach (var section in Sections)
                section.ResetViewState();

            // Not reloading here: the page rebuilds itself from the stored
            // values the next time it is navigated to.
            _isLoaded = false;
        }

        partial void OnSelectedSectionChanged(ISettingsSection? oldValue, ISettingsSection? newValue)
        {
            oldValue?.OnDeactivated();
            newValue?.OnActivated();
        }

        // Every section is rebuilt from the stored values, so this doubles as
        // the Cancel action: whatever was typed is simply overwritten.
        private void LoadFromSettings()
        {
            foreach (var section in Sections)
                section.Load(_agentService.Settings);
        }

        // Invoked by the gear button on a plugin card: show that plugin's section
        // and navigate to its own page, which is pushed on top of Settings so the
        // back button and breadcrumb return here.
        [RelayCommand]
        private void OpenPlugin(PluginSettingsSectionViewModel? section)
        {
            if (section == null)
                return;

            SelectedSection = section;
            _navigation.Navigate<PluginSettingsViewModel>();
        }

        [RelayCommand]
        private void Cancel()
        {
            LoadFromSettings();
            ShowStatus("Changes discarded.", NotificationType.Information);
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            var invalid = Sections.FirstOrDefault(section => section.HasErrors);

            if (invalid != null)
            {
                // Save can be pressed from a plugin page too, so take the user to
                // wherever the offending section lives before showing the error.
                if (invalid is PluginSettingsSectionViewModel plugin)
                    OpenPlugin(plugin);
                else
                    _navigation.Navigate<SettingsViewModel>();

                ShowStatus($"Fix the errors in '{invalid.Title}' before saving.", NotificationType.Error);
                return;
            }

            IsBusy = true;

            try
            {
                // Sections this build does not render yet (plugin settings) must
                // survive the round trip, so start from what is already stored
                // and overwrite only what is on screen.
                var settings = _agentService.Settings.Clone();

                foreach (var section in Sections)
                    section.Save(settings);

                _agentService.SaveSettings(settings);

                // No clearDiscovery: AgentService now deletes exactly the
                // discovery configs that this apply stopped publishing, so
                // saving no longer makes every entity in Home Assistant blink.
                var errors = await _agentService.ApplySettingsAsync();

                // AgentService has already logged each problem with its
                // exception; the user gets the short version.
                if (errors.Count > 0)
                {
                    ShowStatus(string.Join(Environment.NewLine, errors), NotificationType.Warning);
                    return;
                }

                ShowStatus("Settings applied.", NotificationType.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply the settings");
                ShowStatus($"Failed to apply the settings: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ShowStatus(string message, NotificationType severity)
        {
            StatusMessage = message;
            StatusSeverity = severity;
            IsStatusOpen = true;
        }

        private static string GetAssemblyVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? string.Empty;
        }
    }
}
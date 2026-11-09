using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Agent.Services;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.ViewModels.Pages
{
    public partial class DashboardViewModel : ObservableObject, INavigationAware
    {
        private readonly AgentService _agentService;
        private readonly PluginManager _pluginManager;
        private readonly UpdateService _updates;
        private readonly ILogger<DashboardViewModel> _logger;

        public DashboardViewModel(
            AgentService agentService,
            PluginManager pluginManager,
            UpdateService updates,
            ILogger<DashboardViewModel> logger)
        {
            _agentService = agentService;
            _pluginManager = pluginManager;
            _updates = updates;
            _logger = logger;

            // What the elevated plugin step did before it restarted the app.
            if (updates.LastPluginReport is { } report)
                ShowStatus(report.Message, report.Success ? NotificationType.Success : NotificationType.Error);

            updates.Changed += () => Dispatcher.UIThread.Post(RefreshUpdates);
            RefreshUpdates();

            Plugins = new ObservableCollection<PluginStatusViewModel>(
                pluginManager.Plugins.Select(plugin => new PluginStatusViewModel(plugin)));

            LoadErrors = new ObservableCollection<string>(
                pluginManager.Errors.Select(error => $"{error.Source}: {error.Message}"));

            // Pushed rather than polled: drops and automatic reconnects show up
            // the moment they happen. Raised on a background thread.
            agentService.ConnectionChanged += connected =>
                Dispatcher.UIThread.Post(() => IsConnected = connected);
        }

        public string AgentVersion => UpdateService.InstalledVersion;

        public string VersionText => $"Version {AgentVersion}";

        // The agent update from the official catalog, if there is one.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasAgentUpdate))]
        [NotifyPropertyChangedFor(nameof(AgentUpdateTitle))]
        [NotifyPropertyChangedFor(nameof(ReleaseNotes))]
        [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
        private Core.Updates.CatalogAgent? _agentUpdate;

        public bool HasAgentUpdate => AgentUpdate != null;

        public Uri? ReleaseNotes => Uri.TryCreate(AgentUpdate?.ReleaseNotes, UriKind.Absolute, out var uri) ? uri : null;

        public bool HasReleaseNotes => ReleaseNotes != null;

        public string AgentUpdateTitle => $"Version {AgentUpdate?.Version} is available";

        public string AgentUpdateMessage => _updates.CanUpdateAgent
            ? $"You have {AgentVersion}. The update installs in the background and restarts the agent."
            : Core.AgentPaths.IsPortable
                ? $"You have {AgentVersion}. This is a portable copy: replace its folder with the new release."
                : $"You have {AgentVersion}. Download the disk image and replace the app in Applications with the new one.";

        // The release page: release notes where the agent updates itself, the
        // download where it does not (macOS has no installer to run).
        public string ReleaseLinkText => _updates.CanUpdateAgent || Core.AgentPaths.IsPortable ? "Release notes" : "Download";

        public bool CanInstallAgentUpdate => _updates.CanUpdateAgent;

        [ObservableProperty]
        private string _updateStatus = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsNotDownloading))]
        private bool _isDownloading;

        public bool IsNotDownloading => !IsDownloading;

        [ObservableProperty]
        private double _downloadProgress;

        private void RefreshUpdates()
        {
            var result = _updates.Result;

            AgentUpdate = result.AgentUpdate;

            var failed = result.Sources.Count(source => source.Error != null)
                + (result.AgentIndex?.Error != null ? 1 : 0);

            UpdateStatus = _updates.IsChecking
                ? "Checking for updates…"
                : result.CheckedAt == DateTimeOffset.MinValue
                    ? "Not checked yet"
                    : $"Checked {result.CheckedAt:g}"
                      + (result.AgentUpdate == null ? " — up to date" : string.Empty)
                      + (failed > 0 ? $", {failed} catalog(s) unreachable" : string.Empty);
        }

        [RelayCommand]
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                await _updates.CheckAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Update check failed");
                ShowStatus($"Update check failed: {ex.Message}", NotificationType.Warning);
            }
        }

        [RelayCommand]
        private async Task InstallAgentUpdateAsync()
        {
            IsDownloading = true;
            DownloadProgress = 0;

            try
            {
                await _updates.InstallAgentUpdateAsync(new Progress<double>(p => DownloadProgress = p * 100));
                ShowStatus("Installing the update — the agent restarts when it is done.", NotificationType.Information);
            }
            catch (OperationCanceledException)
            {
                ShowStatus("The update needs administrator approval.", NotificationType.Warning);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent update failed");
                ShowStatus($"The update failed: {ex.Message}", NotificationType.Error);
            }
            finally
            {
                IsDownloading = false;
            }
        }

        public ObservableCollection<PluginStatusViewModel> Plugins { get; }

        public ObservableCollection<string> LoadErrors { get; }

        public bool HasLoadErrors => LoadErrors.Count > 0;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ConnectionText))]
        private bool _isConnected;

        public string ConnectionText => IsConnected ? "Connected" : "Not connected";

        [ObservableProperty]
        private string _deviceName = string.Empty;

        [ObservableProperty]
        private string _brokerAddress = string.Empty;

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

        public void OnNavigatedTo() => Refresh();

        public void OnNavigatedFrom() { }

        // The device and broker only change on Save, which happens on another
        // page — re-read whenever this one is shown, and after Reapply here.
        private void Refresh()
        {
            IsConnected = _agentService.IsConnected;

            var general = _agentService.General;

            DeviceName = general.DeviceName is { } name ? AgentService.GetDeviceSlug(name) : "Not configured";
            BrokerAddress = general.MqttBroker is { } broker ? $"{broker}:{general.MqttPort}" : "Not configured";

            // A plugin that stays off raises no EnabledChanged, yet its
            // feature switches still change on Save — so re-read them here.
            foreach (var plugin in Plugins)
                plugin.Refresh();
        }

        [RelayCommand]
        private Task ReapplyAsync() => ApplyAsync(clearDiscovery: false, "Settings reapplied.");

        // The escape hatch for the discovery diff: drops every retained config
        // for this device and republishes from scratch. Needed after an upgrade
        // (no record of what was published before) or if the retained state in
        // Home Assistant has drifted.
        [RelayCommand]
        private Task ClearDiscoveryAsync() =>
            ApplyAsync(clearDiscovery: true, "Discovery cleared and republished.");

        // Starts a replacement process and leaves: the new instance asks this
        // one to shut down, so the page stays busy until the window goes away
        // with it. Useful after dropping in a new plugin dll, which the running
        // process keeps locked.
        [RelayCommand]
        private void Restart()
        {
            IsBusy = true;

            try
            {
                AgentRestart.Start();

                ShowStatus("Restarting…", NotificationType.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start the replacement instance");
                ShowStatus(ex.Message, NotificationType.Error);

                IsBusy = false;
            }
        }

        private async Task ApplyAsync(bool clearDiscovery, string success)
        {
            IsBusy = true;

            try
            {
                var errors = await _agentService.ApplySettingsAsync(clearDiscovery);

                Refresh();

                // AgentService has already logged each problem with its
                // exception; the user gets the short version.
                if (errors.Count > 0)
                {
                    ShowStatus(string.Join(Environment.NewLine, errors), NotificationType.Warning);
                    return;
                }

                ShowStatus(success, NotificationType.Success);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply the settings");
                ShowStatus(ex.Message, NotificationType.Error);
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
    }

    public partial class PluginStatusViewModel : ObservableObject
    {
        private readonly BasePlugin _plugin;

        public PluginStatusViewModel(BasePlugin plugin)
        {
            _plugin = plugin;

            Name = plugin.PluginName;
            Code = plugin.PluginCode;
            _isEnabled = plugin.IsEnabled;
            _details = BuildDetails();

            // Plugins raise this from whatever thread their lifecycle runs on.
            // Features are switched while the plugin is disabled and applied
            // before it is enabled again, so enabling re-reads them too.
            plugin.EnabledChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
        }

        public void Refresh()
        {
            IsEnabled = _plugin.IsEnabled;
            Details = BuildDetails();
        }

        public string Name { get; }

        public string Code { get; }

        // "windows-plugin · 8 of 8 features" — the counts are for this host
        // (the desktop app); the service's features run in the service.
        [ObservableProperty]
        private string _details;

        private string BuildDetails()
        {
            var features = _plugin.Features.Where(feature => feature.Host == _plugin.HostMode).ToArray();

            if (features.Length == 0)
                return $"{Code} · runs in the service only";

            var enabled = features.Count(feature => feature.IsEnabled);
            return $"{Code} · {enabled} of {features.Length} features";
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StateText))]
        private bool _isEnabled;

        public string StateText => IsEnabled ? "Enabled" : "Disabled";
    }
}
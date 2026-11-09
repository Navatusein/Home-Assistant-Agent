using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Core.Settings;
using HomeAssistant.Connector.Helpers;

namespace HomeAssistant.Agent.ViewModels.Settings;

// The application's own settings. Unlike plugin sections this one is not
// descriptor-driven: the layout is hand-written XAML, which buys per-field
// validation and the live identity preview below.
//
// The keys and their string encoding are a contract with AgentService (and
// with the Windows service, which reads the same file), so they must not
// change: see ApplySettingsCoreAsync.
public partial class GeneralSettingsViewModel : ObservableValidator, ISettingsSection
{
    public GeneralSettingsViewModel(WindowsServiceViewModel windowsService)
    {
        WindowsService = windowsService;
    }

    // Managing the service stores nothing, so it rides along in this section
    // rather than owning one of its own.
    public WindowsServiceViewModel WindowsService { get; }

    public void OnActivated() => WindowsService.StartPolling();

    public void OnDeactivated() => WindowsService.StopPolling();

    // The three cards on this page are bound to these rather than expanding
    // themselves, so closing the window can put them all back.
    [ObservableProperty]
    private bool _isDeviceExpanded;

    [ObservableProperty]
    private bool _isConnectionExpanded;

    [ObservableProperty]
    private bool _isServiceExpanded;

    public void ResetViewState()
    {
        IsDeviceExpanded = false;
        IsConnectionExpanded = false;
        IsServiceExpanded = false;
    }

    // Pre-filled for a first start only; an empty field in the stored settings
    // still means "not set".
    private const string DefaultBroker = "homeassistant.local";
    private static readonly string DefaultPort = GeneralSettings.DefaultPort.ToString();
    private const string DefaultRootTopic = GeneralSettings.DefaultRootTopic;

    public string Name => GeneralSettings.Section;

    public string Title => "Settings";

    [ObservableProperty]
    private bool _autoStart;

    [ObservableProperty]
    private bool _checkForUpdates = true;

    // The catalogs the user added; the official one is implied. Edited on the
    // Settings page's plugin area and saved with the rest.
    public System.Collections.ObjectModel.ObservableCollection<string> PluginSources { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddPluginSourceCommand))]
    private string _newPluginSource = string.Empty;

    [ObservableProperty]
    private string _pluginSourceError = string.Empty;

    private bool CanAddPluginSource => !string.IsNullOrWhiteSpace(NewPluginSource);

    [RelayCommand(CanExecute = nameof(CanAddPluginSource))]
    private void AddPluginSource()
    {
        var url = Core.Updates.CatalogClient.NormalizeSource(NewPluginSource);

        if (url == null)
        {
            PluginSourceError = "Enter a GitHub repository (https://github.com/owner/repo) or an https link to an index.json.";
            return;
        }

        if (!Core.Updates.CatalogClient.IsOfficialUrl(url) && !PluginSources.Contains(url, StringComparer.OrdinalIgnoreCase))
            PluginSources.Add(url);

        NewPluginSource = string.Empty;
        PluginSourceError = string.Empty;
    }

    [RelayCommand]
    private void RemovePluginSource(string? url)
    {
        if (url != null)
            PluginSources.Remove(url);
    }

    public IReadOnlyList<string> LogLevels => Core.Logging.LogLevels.Names;

    [ObservableProperty]
    private string _logLevel = Core.Logging.LogLevels.Info;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(DeviceSlug))]
    [NotifyPropertyChangedFor(nameof(DeviceBaseTopic))]
    [Required(ErrorMessage = "The device name is required to connect.")]
    private string _deviceName = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(BrokerSummary))]
    [Required(ErrorMessage = "The MQTT host is required to connect.")]
    private string _mqttBroker = DefaultBroker;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [NotifyPropertyChangedFor(nameof(BrokerSummary))]
    [CustomValidation(typeof(GeneralSettingsViewModel), nameof(ValidatePort))]
    private string _mqttPort = DefaultPort;

    [ObservableProperty]
    private string _mqttUser = string.Empty;

    [ObservableProperty]
    private string _mqttPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceBaseTopic))]
    private string _rootTopic = DefaultRootTopic;

    // Keeps the broker visible while the connection card is collapsed.
    public string BrokerSummary => $"{MqttBroker}:{MqttPort}";

    // Live preview of what the device name turns into. The slug drives the
    // MQTT client id, the topics and the Home Assistant device id, and the
    // transformation is lossy enough (non-ASCII dropped) to be worth showing.
    public string DeviceSlug => AgentService.GetDeviceSlug(DeviceName);

    public string DeviceBaseTopic => MqttTopicHelper.GetDeviceBaseTopic(
        string.IsNullOrWhiteSpace(RootTopic) ? DefaultRootTopic : RootTopic,
        DeviceSlug);

    public static ValidationResult? ValidatePort(string? value, ValidationContext _)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ValidationResult.Success;

        return int.TryParse(value, out var port) && port is > 0 and <= 65535
            ? ValidationResult.Success
            : new ValidationResult("The port must be a number between 1 and 65535.");
    }

    // The keys and their encoding belong to GeneralSettings; this only maps
    // its typed values onto the form's editable strings.
    public void Load(AgentSettings stored)
    {
        var settings = GeneralSettings.FromValues(stored.GetSection(Name));

        AutoStart = settings.AutoStart;
        LogLevel = Core.Logging.LogLevels.ToName(settings.LogLevel);
        DeviceName = settings.DeviceName ?? string.Empty;
        MqttBroker = settings.MqttBroker ?? DefaultBroker;
        MqttPort = settings.MqttPort.ToString();
        MqttUser = settings.MqttUser ?? string.Empty;
        MqttPassword = settings.MqttPassword ?? string.Empty;
        RootTopic = settings.RootTopic;
        CheckForUpdates = settings.CheckForUpdates;

        PluginSources.Clear();
        foreach (var source in settings.PluginSources)
            PluginSources.Add(source);

        NewPluginSource = string.Empty;
        PluginSourceError = string.Empty;

        ValidateAllProperties();
    }

    public void Save(AgentSettings settings) => settings.Sections[Name] = GetValues();

    public Dictionary<string, string> GetValues()
    {
        var settings = new GeneralSettings
        {
            AutoStart = AutoStart,
            LogLevel = Core.Logging.LogLevels.TryParse(LogLevel, out var level) ? level : Serilog.Events.LogEventLevel.Information,
            DeviceName = NullIfBlank(DeviceName),
            MqttBroker = NullIfBlank(MqttBroker),
            MqttPort = int.TryParse(MqttPort, out var port) ? port : GeneralSettings.DefaultPort,
            MqttUser = NullIfBlank(MqttUser),
            MqttPassword = NullIfBlank(MqttPassword),
            RootTopic = NullIfBlank(RootTopic) ?? GeneralSettings.DefaultRootTopic,
            CheckForUpdates = CheckForUpdates,
            PluginSources = PluginSources.ToArray(),
        };

        return settings.ToValues();
    }

    private static string? NullIfBlank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
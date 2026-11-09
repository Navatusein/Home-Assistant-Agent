using HomeAssistant.Agent.Core.Logging;
using Serilog.Events;

namespace HomeAssistant.Agent.Core.Settings;

// The application's own settings — the "general" section of Settings.json —
// as one typed object. The keys and their string encoding are a contract with
// files already on disk (and with the Windows service, which reads the same
// file), so they must not change.
public sealed record GeneralSettings
{
    public const string Section = "general";

    public const int DefaultPort = 1883;
    public const string DefaultRootTopic = "homeassistant-agent";

    private const string AutoStartKey = "AutoStart";
    private const string DeviceNameKey = "DeviceName";
    private const string MqttBrokerKey = "MqttBroker";
    private const string MqttPortKey = "MqttPort";
    private const string MqttUserKey = "MqttUser";
    private const string MqttPasswordKey = "MqttPassword";
    private const string RootTopicKey = "RootTopic";
    private const string LogLevelKey = "LogLevel";
    private const string CheckForUpdatesKey = "CheckForUpdates";
    private const string PluginSourcesKey = "PluginSources";

    public bool AutoStart { get; init; }

    // Look for agent and plugin updates on start and every few hours.
    public bool CheckForUpdates { get; init; } = true;

    // Plugin catalogs the user added (index.json URLs), besides the official
    // one, which is always there.
    public IReadOnlyList<string> PluginSources { get; init; } = [];

    // null when not set yet: the agent then does not connect at all.
    public string? DeviceName { get; init; }

    public string? MqttBroker { get; init; }

    public int MqttPort { get; init; } = DefaultPort;

    public string? MqttUser { get; init; }

    public string? MqttPassword { get; init; }

    public string RootTopic { get; init; } = DefaultRootTopic;

    public LogEventLevel LogLevel { get; init; } = LogEventLevel.Information;

    public bool CanConnect => DeviceName != null && MqttBroker != null;

    // Missing, blank or unparsable values fall back to the defaults above.
    public static GeneralSettings FromValues(IReadOnlyDictionary<string, string>? values)
    {
        values ??= new Dictionary<string, string>();

        return new GeneralSettings
        {
            AutoStart = bool.TryParse(values.GetValueOrDefault(AutoStartKey), out var autoStart) && autoStart,
            DeviceName = NullIfBlank(values.GetValueOrDefault(DeviceNameKey)),
            MqttBroker = NullIfBlank(values.GetValueOrDefault(MqttBrokerKey)),
            MqttPort = int.TryParse(values.GetValueOrDefault(MqttPortKey), out var port) && port is > 0 and <= 65535
                ? port
                : DefaultPort,
            MqttUser = NullIfBlank(values.GetValueOrDefault(MqttUserKey)),
            MqttPassword = NullIfBlank(values.GetValueOrDefault(MqttPasswordKey)),
            RootTopic = NullIfBlank(values.GetValueOrDefault(RootTopicKey)) ?? DefaultRootTopic,
            LogLevel = LogLevels.TryParse(values.GetValueOrDefault(LogLevelKey), out var level)
                ? level
                : LogEventLevel.Information,
            // Missing (settings from before updates existed) means on.
            CheckForUpdates = !bool.TryParse(values.GetValueOrDefault(CheckForUpdatesKey), out var check) || check,
            PluginSources = ParseList(values.GetValueOrDefault(PluginSourcesKey)),
        };
    }

    private static string[] ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(json)?
                .Where(item => !string.IsNullOrWhiteSpace(item)).ToArray() ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    // bool.ToString gives "True"/"False" — the encoding the files already use.
    public Dictionary<string, string> ToValues() => new()
    {
        [AutoStartKey] = AutoStart.ToString(),
        [DeviceNameKey] = DeviceName ?? string.Empty,
        [MqttBrokerKey] = MqttBroker ?? string.Empty,
        [MqttPortKey] = MqttPort.ToString(),
        [MqttUserKey] = MqttUser ?? string.Empty,
        [MqttPasswordKey] = MqttPassword ?? string.Empty,
        [RootTopicKey] = RootTopic,
        [LogLevelKey] = LogLevels.ToName(LogLevel),
        [CheckForUpdatesKey] = CheckForUpdates.ToString(),
        [PluginSourcesKey] = System.Text.Json.JsonSerializer.Serialize(PluginSources),
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

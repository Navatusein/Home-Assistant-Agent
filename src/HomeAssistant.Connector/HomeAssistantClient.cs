using HomeAssistant.Connector.Entities;
using HomeAssistant.Connector.Enums;
using HomeAssistant.Connector.Helpers;
using HomeAssistantDiscoveryNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MqttClient = HomeAssistant.Connector.Mqtt.MqttClient;

namespace HomeAssistant.Connector;

public class HomeAssistantClient
{
    private readonly MqttClient _mqttClient = new();
    private readonly List<IEntity> _entities = [];
    private readonly List<string> _discoveryTopics = [];

    private ILogger _logger = NullLogger.Instance;

    // Optional: without a factory the client and its MQTT connection log
    // nothing.
    public ILoggerFactory? LoggerFactory
    {
        get;
        init
        {
            field = value;
            _logger = value?.CreateLogger<HomeAssistantClient>() ?? (ILogger) NullLogger.Instance;
            _mqttClient.Logger = value?.CreateLogger<MqttClient>() ?? (ILogger) NullLogger.Instance;
        }
    }

    public required string MqttDevice { get; init; }
    public required string MqttRootTopic { get; init; }
    public required string DeviceIdPrefix { get; init; }
    public required string DeviceName { get; init; }
    public required string DeviceModel { get; init; }
    public required string DeviceManufacturer { get; init; }
    public required string DeviceSoftwareVersion { get; init; }

    // Overrides the device-registry identifier (defaults to MqttDevice).
    // A second host session (e.g. the Windows service) sets this to the
    // primary session's id so Home Assistant merges both entity sets into
    // one device while their topics and availability stay separate.
    public string? DeviceIdentifier { get; init; }

    public bool IsConnected => _mqttClient.IsConnected;

    // See MqttClient.ConnectionChanged.
    public event Action<bool>? ConnectionChanged
    {
        add => _mqttClient.ConnectionChanged += value;
        remove => _mqttClient.ConnectionChanged -= value;
    }

    // Discovery topics this client published, in registration order. The host
    // compares them against the previous run to delete exactly the configs that
    // disappeared, instead of wiping the device's discovery and republishing.
    public IReadOnlyList<string> PublishedDiscoveryTopics => _discoveryTopics;

    public HomeAssistantClient()
    {
        // After an automatic reconnect the broker has already published the
        // last will (retained "offline"), so announce the device again.
        _mqttClient.ReconnectedAsync += PublishAvailabilityAsync;
    }

    // Enough for a broker on the local network to answer.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    public async Task<bool> ConnectToMqttAsync(string broker, int port, string? username = null, string? password = null)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(broker, port)
            // An unreachable broker would otherwise hold a connect attempt for
            // the system's TCP timeout (about 75 s on macOS), and with it the
            // settings Save; the background retries take over after this.
            .WithTimeout(ConnectTimeout)
            .WithClientId(MqttDevice)
            .WithCleanSession()
            .WithWillTopic(MqttTopicHelper.GetAvailabilityTopic(MqttRootTopic, MqttDevice))
            .WithWillRetain()
            .WithWillPayload((string) MqttAvailabilityEnum.Offline);

        if (username != null && password != null)
            options = options.WithCredentials(username, password);

        _logger.LogInformation("Connecting to MQTT broker {Broker}:{Port} as {ClientId} ({Auth})",
            broker, port, MqttDevice, username != null ? "user " + username : "anonymous");

        if (!await _mqttClient.ConnectAsync(options.Build()))
            return false;

        _logger.LogInformation("Connected to MQTT broker {Broker}:{Port}", broker, port);
        return true;
    }

    // Publishes the offline availability and closes the MQTT connection.
    // Call before discarding the client (e.g. when settings change and a new
    // client is built).
    public async Task DisconnectAsync()
    {
        _logger.LogInformation("Disconnecting from MQTT ({ClientId})", MqttDevice);

        try
        {
            await _mqttClient.PublishAsync(
                MqttTopicHelper.GetAvailabilityTopic(MqttRootTopic, MqttDevice),
                (string) MqttAvailabilityEnum.Offline,
                retain: true);
        }
        catch (InvalidOperationException)
        {
            // Already disconnected — nothing to announce.
        }

        await _mqttClient.DisconnectAsync();
    }

    // Drops every retained discovery config previously published for this
    // device. Blunt and slow (it subscribes and waits for the broker to replay
    // the retained messages), so it is reserved for an explicit reset and for
    // leaving a broker behind — the usual path is RemoveDiscoveryAsync with the
    // topics that actually went away.
    public async Task ClearDiscoveryAsync(TimeSpan? collectWindow = null)
    {
        var filter = MqttTopicHelper.GetDiscoveryTopicFilter(MqttDevice);
        await _mqttClient.ClearRetainedAsync(filter, collectWindow ?? TimeSpan.FromSeconds(1));
    }

    // Deletes specific discovery configs. An empty retained payload is how MQTT
    // discovery removes an entity: Home Assistant drops it, and the broker
    // forgets the retained message.
    public async Task RemoveDiscoveryAsync(IEnumerable<string> topics)
    {
        foreach (var topic in topics)
        {
            _logger.LogDebug("Removing discovery {Topic}", topic);
            await _mqttClient.PublishAsync(topic, string.Empty, retain: true);
        }
    }

    // Registers a batch of entities and publishes their discovery config plus
    // initial MQTT wiring (state + command topics). Plugins call this from
    // their enable step to expose their entities to Home Assistant.
    public async Task RegisterEntitiesAsync(IEnumerable<IEntity> entities)
    {
        foreach (var entity in entities)
        {
            if (await PublishEntityDiscoveryAsync(entity))
                _entities.Add(entity);
        }
    }

    // Publishes the discovery of entities whose owner is switched off and marks
    // them unavailable, so they stay in Home Assistant instead of disappearing.
    // Unlike RegisterEntitiesAsync nothing is wired up: no initial state and no
    // command subscriptions, since there is nothing running to handle them.
    public async Task PublishUnavailableEntitiesAsync(IEnumerable<IEntity> entities)
    {
        var deviceAvailability = EntityContext.DeviceAvailability;

        foreach (var entity in entities)
        {
            entity.Bind(EntityContext);
            var config = entity.GetDiscovery();

            if (!TryClaimDiscoveryTopic(config))
                continue;

            await _mqttClient.PublishDiscoveryAsync(MqttDevice, config);

            _logger.LogDebug("Published {UniqueId} ({Component}) as unavailable", config.UniqueId, config.Component);

            // The entity's own availability topic; the device-wide one stays
            // online for the rest of the device.
            foreach (var availability in config.Availability ?? [])
            {
                if (availability.Topic != deviceAvailability.Topic)
                    await _mqttClient.PublishAsync(availability.Topic, (string) MqttAvailabilityEnum.Offline, retain: true);
            }
        }
    }

    // Re-publishes the discovery config of an already registered entity, for
    // the ones whose config is not fixed when they are registered — a select
    // whose options only become known once the app it mirrors is running. The
    // topic stays the same, so Home Assistant updates the entity in place and
    // the discovery ledger needs no update.
    //
    // The MQTT wiring is deliberately left alone: the entity keeps its
    // subscription and its state.
    public async Task<bool> RefreshDiscoveryAsync(IEntity entity)
    {
        if (!_entities.Contains(entity))
            return false;

        var config = entity.GetDiscovery();

        _logger.LogDebug("Refreshing discovery of {UniqueId}", config.UniqueId);

        return await _mqttClient.PublishDiscoveryAsync(MqttDevice, config);
    }

    public async Task<bool> PublishAvailabilityAsync()
    {
        return await _mqttClient.PublishAvailabilityAsync(MqttTopicHelper.GetAvailabilityTopic(MqttRootTopic, MqttDevice));
    }

    private async Task<bool> PublishEntityDiscoveryAsync(IEntity entity)
    {
        entity.Bind(EntityContext);
        var config = entity.GetDiscovery();

        // A duplicate is left detached: its state updates go nowhere instead of
        // overwriting the first entity's topics.
        if (!TryClaimDiscoveryTopic(config))
            return false;

        await entity.InitAsync(_mqttClient);
        await _mqttClient.PublishDiscoveryAsync(MqttDevice, config);

        _logger.LogDebug("Registered {UniqueId} ({Component})", config.UniqueId, config.Component);
        return true;
    }

    // Two entities with the same unique id (two launchers named alike, say)
    // would share one discovery config, so Home Assistant would only ever see
    // the last one — while both answer the same command topic. The first one
    // wins; the second is skipped with a pointer to the cause.
    private bool TryClaimDiscoveryTopic(MqttDiscoveryConfig config)
    {
        var topic = MqttTopicHelper.GetDiscoveryTopic(MqttDevice, config);

        if (_discoveryTopics.Contains(topic))
        {
            _logger.LogWarning("Skipped \"{Name}\": its id {UniqueId} is already taken by another entity — give one of them a different name",
                config.Name, config.UniqueId);
            return false;
        }

        _discoveryTopics.Add(topic);
        return true;
    }

    // The same for every entity of this session; built on first use, once the
    // init-only device properties are set.
    private EntityContext EntityContext => field ??= new EntityContext(
        BuildDiscoveryDevice(),
        BuildDeviceAvailability(),
        MqttTopicHelper.GetDeviceBaseTopic(MqttRootTopic, MqttDevice),
        DeviceIdPrefix);

    private MqttDiscoveryDevice BuildDiscoveryDevice()
    {
        return new MqttDiscoveryDevice
        {
            Name = DeviceName,
            Identifiers = [DeviceIdentifier ?? MqttDevice],
            Manufacturer = DeviceManufacturer,
            Model = DeviceModel,
            SoftwareVersion = DeviceSoftwareVersion
        };
    }

    private MqttDiscoveryAvailablilty BuildDeviceAvailability()
    {
        return new MqttDiscoveryAvailablilty
        {
            Topic = MqttTopicHelper.GetAvailabilityTopic(MqttRootTopic, MqttDevice),
            PayloadAvailable = (string) MqttAvailabilityEnum.Online,
            PayloadNotAvailable = (string) MqttAvailabilityEnum.Offline
        };
    }
}

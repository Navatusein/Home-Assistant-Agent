using HomeAssistant.Connector.Enums;
using HomeAssistant.Connector.Helpers;
using HomeAssistant.Connector.Mqtt;
using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

// What every entity has: a name, an id, topics derived from where it is bound,
// its own availability, and the common part of its discovery config. A
// concrete entity adds its component-specific discovery fields
// (CreateDiscovery<T>) and what it publishes or listens to.
public abstract class BaseEntity: IEntity
{
    // Until InitAsync — and again after Detach — the entity publishes into the
    // null client: an entity of a feature that is switched off can be updated
    // by plugin code without a guard, and simply goes nowhere.
    protected IMqttClient MqttClient = NullMqttClient.Instance;

    private EntityContext? _context;

    public required string Name { get; init; }
    public required string UniqueId { get; init; }
    public required string BaseTopic { get; init; }
    public string? Icon { get; init; }

    // False for an entity whose feature publishes the real state as soon as
    // it is enabled: the default would show up first for a moment — a false
    // "locked" or "volume 0" that automations in Home Assistant react to.
    public bool PublishDefaultState { get; init; } = true;

    protected EntityContext Context =>
        _context ?? throw new InvalidOperationException($"Entity '{UniqueId}' is used before it was bound to a device.");

    public void Bind(EntityContext context)
    {
        _context = context;
    }

    public abstract MqttDiscoveryConfig GetDiscovery();

    public virtual async Task InitAsync(IMqttClient mqttClient)
    {
        MqttClient = mqttClient;

        await SetAvailableAsync(false);
        if (PublishDefaultState)
            await PublishInitialStateAsync();

        await SubscribeControlTopicAsync();
    }

    public virtual async Task<bool> SetAvailableAsync(bool available)
    {
        var payload = (string) (available ? MqttAvailabilityEnum.Online : MqttAvailabilityEnum.Offline);
        return await MqttClient.PublishAsync(GetAvailabilityTopic(), payload, retain: true);
    }

    public void Detach()
    {
        MqttClient = NullMqttClient.Instance;
    }

    // The discovery fields every entity shares. Those are init-only in the
    // library, so the common part has to be set while the config is created.
    protected T CreateDiscovery<T>() where T : MqttDiscoveryConfig, new()
    {
        var availability = GetAvailability();

        return new T
        {
            Device = Context.Device,
            Name = Name,
            UniqueId = $"{Context.DeviceIdPrefix}_{UniqueId}",
            Icon = Icon,
            Availability = availability,
            AvailabilityMode = availability == null ? null : MqttDiscoveryAvailabilityMode.All,
        };
    }

    // Available only while both the device (the host session) and the entity
    // itself are — so a plugin can take its own entities offline. null for an
    // entity that must not follow availability at all.
    protected virtual List<MqttDiscoveryAvailablilty>? GetAvailability() =>
        [Context.DeviceAvailability, GetEntityAvailability()];

    protected virtual Task PublishInitialStateAsync()
    {
        return Task.CompletedTask;
    }

    protected virtual Task SubscribeControlTopicAsync()
    {
        return Task.CompletedTask;
    }

    protected string GetStateTopic()
    {
        return MqttTopicHelper.NormalizeTopic($"{GetBaseTopic()}/state");
    }

    protected string GetCommandTopic()
    {
        return MqttTopicHelper.NormalizeTopic($"{GetBaseTopic()}/command");
    }

    protected string GetAvailabilityTopic()
    {
        return MqttTopicHelper.NormalizeTopic($"{GetBaseTopic()}/availability");
    }

    private MqttDiscoveryAvailablilty GetEntityAvailability()
    {
        return new MqttDiscoveryAvailablilty
        {
            Topic = GetAvailabilityTopic(),
            PayloadAvailable = (string) MqttAvailabilityEnum.Online,
            PayloadNotAvailable = (string) MqttAvailabilityEnum.Offline
        };
    }

    private string GetBaseTopic()
    {
        return MqttTopicHelper.NormalizeTopic($"{Context.DeviceBaseTopic}/{BaseTopic}");
    }
}

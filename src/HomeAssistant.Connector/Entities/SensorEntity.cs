using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

public class SensorEntity: BaseEntity
{
    public string? DeviceClass { get; init; }

    // Required by some device classes for Home Assistant to render the value
    // properly — "%" for battery, for instance.
    public string? UnitOfMeasurement { get; init; }

    public required string DefaultState { get; init; }

    public override MqttDiscoveryConfig GetDiscovery()
    {
        var config = CreateDiscovery<MqttSensorDiscoveryConfig>();

        config.DeviceClass = DeviceClass;
        config.UnitOfMeasurement = UnitOfMeasurement;
        config.StateTopic = GetStateTopic();

        return config;
    }

    // Retained so Home Assistant picks the state up as soon as it subscribes
    // after processing the discovery config — otherwise a re-created entity
    // shows Unknown until the next state change.
    public async Task<bool> SetStateAsync(string state)
    {
        return await MqttClient.PublishAsync(GetStateTopic(), state, retain: true);
    }

    protected override async Task PublishInitialStateAsync()
    {
        await SetStateAsync(DefaultState);
    }
}

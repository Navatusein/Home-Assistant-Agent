using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

public class BinarySensorEntity: BaseEntity
{
    protected const string PayloadOn = "ON";
    protected const string PayloadOff = "OFF";

    public string? DeviceClass { get; init; }
    public bool DefaultState { get; init; }

    public override MqttDiscoveryConfig GetDiscovery()
    {
        var config = CreateDiscovery<MqttBinarySensorDiscoveryConfig>();

        config.DeviceClass = DeviceClass;
        config.PayloadOn = PayloadOn;
        config.PayloadOff = PayloadOff;
        config.StateTopic = GetStateTopic();

        return config;
    }

    // Retained so Home Assistant picks the state up as soon as it subscribes
    // after processing the discovery config (see SensorEntity.SetStateAsync).
    public async Task<bool> SetStateAsync(bool state)
    {
        return await MqttClient.PublishAsync(GetStateTopic(), state ? PayloadOn : PayloadOff, retain: true);
    }

    protected override async Task PublishInitialStateAsync()
    {
        await SetStateAsync(DefaultState);
    }
}

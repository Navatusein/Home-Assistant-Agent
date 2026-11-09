using HomeAssistant.Connector.Mqtt;
using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

// A binary sensor whose state topic IS the host session's availability topic:
// the retained online/offline payloads plus the broker's last will flip the
// state with no publisher running, so the sensor reports "off" while the host
// is down instead of going unavailable ("is the PC on" style sensors). It has
// no availability of its own — it is meaningful precisely when everything
// else on the device is offline.
public class AvailabilityBinarySensorEntity: BinarySensorEntity
{
    public override MqttDiscoveryConfig GetDiscovery()
    {
        var config = CreateDiscovery<MqttBinarySensorDiscoveryConfig>();
        var availability = Context.DeviceAvailability;

        config.DeviceClass = DeviceClass;
        config.StateTopic = availability.Topic;
        config.PayloadOn = (string?) availability.PayloadAvailable;
        config.PayloadOff = (string?) availability.PayloadNotAvailable;

        return config;
    }

    protected override List<MqttDiscoveryAvailablilty>? GetAvailability() => null;

    // The availability topic is owned by the host session (last will and the
    // client's explicit publishes) — the entity must never write to it, so
    // both the initial state publish and the availability updates are no-ops.
    public override Task InitAsync(IMqttClient mqttClient)
    {
        MqttClient = mqttClient;
        return Task.CompletedTask;
    }

    public override Task<bool> SetAvailableAsync(bool available)
    {
        return Task.FromResult(true);
    }
}

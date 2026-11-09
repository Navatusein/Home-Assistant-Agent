using HomeAssistant.Connector.Mqtt;
using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

// The lifecycle HomeAssistantClient drives: Bind once per session, then the
// discovery config, then InitAsync wires state and commands to the MQTT
// client; Detach ends it.
public interface IEntity
{
    // Places the entity on a device; its topics and ids follow from this.
    public void Bind(EntityContext context);

    // The discovery config for Home Assistant. Needs Bind first; no side
    // effects, so it can be built again whenever it changes (RefreshDiscovery).
    public MqttDiscoveryConfig GetDiscovery();

    public Task InitAsync(IMqttClient mqttClient);

    public Task<bool> SetAvailableAsync(bool available);

    // Drops the MQTT client set by InitAsync: until the next InitAsync every
    // publish is a quiet no-op. Called when the client's session ends, so a
    // late state change cannot reach a client that is already disconnected.
    public void Detach();
}

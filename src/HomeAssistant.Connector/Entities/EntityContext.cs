using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

// Where an entity lives: the Home Assistant device it belongs to, that
// device's availability topic, the MQTT base topic its own topics hang under,
// and the prefix that makes its unique id unique across devices. Handed to
// the entity by HomeAssistantClient (IEntity.Bind) before anything else.
public sealed record EntityContext(
    MqttDiscoveryDevice Device,
    MqttDiscoveryAvailablilty DeviceAvailability,
    string DeviceBaseTopic,
    string DeviceIdPrefix);

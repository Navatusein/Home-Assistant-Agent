using HomeAssistantDiscoveryNet;
using MQTTnet;

namespace HomeAssistant.Connector.Mqtt;

// The client of an entity that is not attached to a session (never registered,
// or detached when its plugin was disabled). Publishing and subscribing are
// quiet no-ops that report failure, so plugin code may update entities of a
// switched-off feature without checking first. Connection management on it is
// a programming error and still throws.
public sealed class NullMqttClient: IMqttClient
{
    private const string ExceptionMessage = "The entity is not attached to an MQTT session.";

    public static NullMqttClient Instance { get; } = new();

    private NullMqttClient() { }

    public bool IsConnected => false;

    public Task<bool> ConnectAsync(MqttClientOptions options)
    {
        throw new InvalidOperationException(ExceptionMessage);
    }

    public Task DisconnectAsync()
    {
        throw new InvalidOperationException(ExceptionMessage);
    }

    public Task<bool> PublishAsync(string topic, string message, bool retain = false) => Task.FromResult(false);

    public Task<bool> PublishAvailabilityAsync(string topic) => Task.FromResult(false);

    public Task<bool> PublishDiscoveryAsync(string deviceTopic, MqttDiscoveryConfig config) => Task.FromResult(false);

    public Task ClearRetainedAsync(string topicFilter, TimeSpan collectWindow)
    {
        throw new InvalidOperationException(ExceptionMessage);
    }

    public Task<bool> SubscribeAsync(string topic, MqttMessageHandler messageHandler,
        MqttSubscribeFlags flags = MqttSubscribeFlags.None) => Task.FromResult(false);
}

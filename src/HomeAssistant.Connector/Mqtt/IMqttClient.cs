using HomeAssistantDiscoveryNet;
using MQTTnet;

namespace HomeAssistant.Connector.Mqtt;

public delegate Task MqttMessageHandler(string topic, string message);

public interface IMqttClient
{
    public bool IsConnected { get; }

    public Task<bool> ConnectAsync(MqttClientOptions options);

    public Task DisconnectAsync();

    public Task<bool> PublishAsync(string topic, string message, bool retain = false);

    public Task<bool> PublishAvailabilityAsync(string topic);

    public Task<bool> PublishDiscoveryAsync(string deviceTopic, MqttDiscoveryConfig config);

    public Task ClearRetainedAsync(string topicFilter, TimeSpan collectWindow);

    public Task<bool> SubscribeAsync(string topic, MqttMessageHandler messageHandler,
        MqttSubscribeFlags flags = MqttSubscribeFlags.None);
}
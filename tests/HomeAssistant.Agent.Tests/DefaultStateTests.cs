using HomeAssistant.Connector.Entities;
using HomeAssistant.Connector.Mqtt;
using HomeAssistantDiscoveryNet;
using MqttClientOptions = MQTTnet.MqttClientOptions;

namespace HomeAssistant.Agent.Tests;

public class DefaultStateTests
{
    private const string StateTopic = "pc/test/session/state";

    [Fact]
    public async Task DefaultStateIsPublishedByDefault()
    {
        var client = new RecordingClient();
        var sensor = CreateSensor(publishDefault: true);

        await sensor.InitAsync(client);

        Assert.Contains((StateTopic, "OFF"), client.Published);
    }

    [Fact]
    public async Task DefaultStateCanBeSkipped()
    {
        var client = new RecordingClient();
        var sensor = CreateSensor(publishDefault: false);

        await sensor.InitAsync(client);

        Assert.DoesNotContain(client.Published, message => message.Topic == StateTopic);

        // The feature's own state still goes out.
        await sensor.SetStateAsync(true);
        Assert.Contains((StateTopic, "ON"), client.Published);
    }

    private static BinarySensorEntity CreateSensor(bool publishDefault)
    {
        var sensor = new BinarySensorEntity
        {
            Name = "User Session",
            UniqueId = "test_session",
            BaseTopic = "test/session",
            DefaultState = false,
            PublishDefaultState = publishDefault
        };

        sensor.Bind(new EntityContext(
            new MqttDiscoveryDevice { Name = "PC", Identifiers = ["pc"] },
            new MqttDiscoveryAvailablilty { Topic = "pc/availability" },
            "pc",
            "pc"));

        return sensor;
    }

    private sealed class RecordingClient : IMqttClient
    {
        public List<(string Topic, string Message)> Published { get; } = [];

        public bool IsConnected => true;

        public Task<bool> ConnectAsync(MqttClientOptions options) => Task.FromResult(true);

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<bool> PublishAsync(string topic, string message, bool retain = false)
        {
            Published.Add((topic, message));
            return Task.FromResult(true);
        }

        public Task<bool> PublishAvailabilityAsync(string topic) => Task.FromResult(true);

        public Task<bool> PublishDiscoveryAsync(string deviceTopic, MqttDiscoveryConfig config) =>
            Task.FromResult(true);

        public Task ClearRetainedAsync(string topicFilter, TimeSpan collectWindow) => Task.CompletedTask;

        public Task<bool> SubscribeAsync(string topic, MqttMessageHandler messageHandler,
            MqttSubscribeFlags flags = MqttSubscribeFlags.None) => Task.FromResult(true);
    }
}

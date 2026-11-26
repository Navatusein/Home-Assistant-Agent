using HomeAssistant.Connector.Entities;
using HomeAssistant.Connector.Mqtt;
using HomeAssistantDiscoveryNet;
using MqttClientOptions = MQTTnet.MqttClientOptions;

namespace HomeAssistant.Agent.Tests;

public class ImageEntityTests
{
    private const string StateTopic = "pc/media/cover/state";

    [Fact]
    public void DiscoveryPointsAtTheBase64StateTopic()
    {
        var image = CreateImage();

        var config = Assert.IsType<MqttImageDiscoveryConfig>(image.GetDiscovery());

        Assert.Equal(StateTopic, config.ImageTopic);
        Assert.Equal("b64", config.ImageEncoding);
        Assert.Equal("image/png", config.ContentType);
    }

    [Fact]
    public async Task ImageIsPublishedBase64AndRetained()
    {
        var client = new RecordingClient();
        var image = CreateImage();
        await image.InitAsync(client);

        await image.SetImageAsync(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        Assert.Contains((StateTopic, "iVBORw==", true), client.Published);
    }

    [Fact]
    public async Task NothingIsPublishedBeforeTheFirstImage()
    {
        var client = new RecordingClient();
        var image = CreateImage();

        await image.InitAsync(client);

        Assert.DoesNotContain(client.Published, message => message.Topic == StateTopic);
    }

    [Fact]
    public async Task EmptyImageIsRejected()
    {
        var image = CreateImage();
        await image.InitAsync(new RecordingClient());

        await Assert.ThrowsAsync<ArgumentException>(() => image.SetImageAsync(ReadOnlyMemory<byte>.Empty));
    }

    private static ImageEntity CreateImage()
    {
        var image = new ImageEntity
        {
            Name = "Media Cover",
            UniqueId = "test_cover",
            BaseTopic = "media/cover",
            ContentType = "image/png"
        };

        image.Bind(new EntityContext(
            new MqttDiscoveryDevice { Name = "PC", Identifiers = ["pc"] },
            new MqttDiscoveryAvailablilty { Topic = "pc/availability" },
            "pc",
            "pc"));

        return image;
    }

    private sealed class RecordingClient : IMqttClient
    {
        public List<(string Topic, string Message, bool Retain)> Published { get; } = [];

        public bool IsConnected => true;

        public Task<bool> ConnectAsync(MqttClientOptions options) => Task.FromResult(true);

        public Task DisconnectAsync() => Task.CompletedTask;

        public Task<bool> PublishAsync(string topic, string message, bool retain = false)
        {
            Published.Add((topic, message, retain));
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

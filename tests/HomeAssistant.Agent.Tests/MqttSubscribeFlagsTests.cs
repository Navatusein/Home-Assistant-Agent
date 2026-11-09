using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using HomeAssistant.Connector.Mqtt;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Server;
using MqttClient = HomeAssistant.Connector.Mqtt.MqttClient;

namespace HomeAssistant.Agent.Tests;

public sealed class MqttSubscribeFlagsTests : IAsyncLifetime
{
    private const string Topic = "test/clipboard/text/command";
    private const string Secret = "hunter2-secret";

    private readonly int _port = FreePort();
    private readonly ListLogger _logger = new();
    private MqttServer _server = null!;
    private MqttClient _client = null!;

    public async Task InitializeAsync()
    {
        _server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(_port).Build());
        await _server.StartAsync();

        _client = new MqttClient { Logger = _logger };
        await _client.ConnectAsync(Options());
    }

    public async Task DisposeAsync()
    {
        await _client.DisconnectAsync();
        await _server.StopAsync();
        _server.Dispose();
    }

    [Fact]
    public async Task RetainedMessageIsIgnoredAndCleared()
    {
        await PublishRawAsync(Secret, retain: true);

        var received = new ConcurrentQueue<string>();
        var cleared = new TaskCompletionSource();

        await _client.SubscribeAsync(Topic, (_, message) =>
        {
            received.Enqueue(message);

            // The clearing publish comes back as an empty live message.
            if (message.Length == 0)
                cleared.TrySetResult();

            return Task.CompletedTask;
        }, MqttSubscribeFlags.IgnoreRetained | MqttSubscribeFlags.PrivatePayload);

        await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.DoesNotContain(Secret, received);
        Assert.Empty(await _server.GetRetainedMessagesAsync());
        Assert.DoesNotContain(_logger.Messages, m => m.Contains(Secret));
    }

    [Fact]
    public async Task PrivatePayloadIsDeliveredButNotLogged()
    {
        var delivered = new TaskCompletionSource<string>();

        await _client.SubscribeAsync(Topic, (_, message) =>
        {
            delivered.TrySetResult(message);
            return Task.CompletedTask;
        }, MqttSubscribeFlags.IgnoreRetained | MqttSubscribeFlags.PrivatePayload);

        await PublishRawAsync(Secret, retain: false);

        Assert.Equal(Secret, await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.DoesNotContain(_logger.Messages, m => m.Contains(Secret));
        Assert.Contains(_logger.Messages, m => m.Contains($"<{Secret.Length} chars>"));
    }

    private async Task PublishRawAsync(string payload, bool retain)
    {
        using var publisher = new MqttClientFactory().CreateMqttClient();

        await publisher.ConnectAsync(Options());
        await publisher.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(Topic)
            .WithPayload(payload)
            .WithRetainFlag(retain)
            .Build());
        await publisher.DisconnectAsync();
    }

    private MqttClientOptions Options()
    {
        return new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", _port).Build();
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class ListLogger : ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Enqueue(formatter(state, exception));
        }
    }
}

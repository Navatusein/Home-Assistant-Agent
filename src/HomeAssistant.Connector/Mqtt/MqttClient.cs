using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using HomeAssistant.Connector.Enums;
using HomeAssistant.Connector.Helpers;
using HomeAssistantDiscoveryNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;

namespace HomeAssistant.Connector.Mqtt;

public class MqttClient: IMqttClient
{
    private static readonly TimeSpan InitialReconnectDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(60);

    private readonly MQTTnet.IMqttClient _mqttClient;

    // Written by the entities as they register and read by MQTTnet's receive
    // thread and the reconnect loop at the same time.
    private readonly ConcurrentDictionary<string, Subscription> _messageHandlers = new();

    private MqttClientOptions? _options;
    private CancellationTokenSource? _reconnectCancellation;
    private int _reconnecting;

    // Raised after the connection was re-established following an unexpected
    // drop. Subscriptions are already restored when it fires; use it to
    // re-publish state the broker's last will has overwritten (availability).
    public event Func<Task>? ReconnectedAsync;

    // True on connect and on a successful reconnect, false when the connection
    // drops or is closed. Fires on whatever thread noticed the change.
    public event Action<bool>? ConnectionChanged;

    public MqttClient()
    {
        var factory = new MqttClientFactory();
        _mqttClient = factory.CreateMqttClient();

        _mqttClient.ApplicationMessageReceivedAsync += HandleMessageReceived;
        _mqttClient.DisconnectedAsync += HandleDisconnected;
    }

    public ILogger Logger { get; set; } = NullLogger.Instance;

    public bool IsConnected => _mqttClient.IsConnected;

    public async Task<bool> ConnectAsync(MqttClientOptions options)
    {
        var connectResult = await _mqttClient.ConnectAsync(options);

        if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
        {
            Logger.LogWarning("MQTT broker refused the connection: {ResultCode} {Reason}",
                connectResult.ResultCode, connectResult.ReasonString);
            return false;
        }

        _options = options;
        _reconnectCancellation = new CancellationTokenSource();

        ConnectionChanged?.Invoke(true);

        return true;
    }

    public async Task DisconnectAsync()
    {
        if (_reconnectCancellation != null)
        {
            await _reconnectCancellation.CancelAsync();
            _reconnectCancellation = null;
        }

        _messageHandlers.Clear();

        if (_mqttClient.IsConnected)
        {
            await _mqttClient.DisconnectAsync();
            ConnectionChanged?.Invoke(false);
        }
    }

    public async Task<bool> PublishAsync(string topic, string message, bool retain = false)
    {
        CheckConnected();

        var result = await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(message)
            .WithRetainFlag(retain)
            .Build());

        if (!result.IsSuccess)
            Logger.LogWarning("Publish to {Topic} failed: {ReasonCode} {Reason}", topic, result.ReasonCode, result.ReasonString);

        return result.IsSuccess;
    }

    public async Task<bool> PublishAvailabilityAsync(string topic)
    {
        var result = await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload((string) MqttAvailabilityEnum.Online)
            .WithRetainFlag()
            .Build());

        if (!result.IsSuccess)
            Logger.LogWarning("Publishing availability to {Topic} failed: {ReasonCode} {Reason}", topic, result.ReasonCode, result.ReasonString);

        return result.IsSuccess;
    }

    public async Task<bool> PublishDiscoveryAsync(string deviceTopic, MqttDiscoveryConfig config)
    {
        CheckConnected();

        var context = MqttDiscoveryJsonContext.Default;

        var jsonTypeInfo = context.GetTypeInfo(config.GetType());

        if (jsonTypeInfo == null)
            throw new InvalidOperationException("Invalid Json Context");

        var topic = MqttTopicHelper.GetDiscoveryTopic(deviceTopic, config);

        var result = await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonSerializer.Serialize(config, jsonTypeInfo))
            .WithRetainFlag()
            .Build());

        if (!result.IsSuccess)
            Logger.LogWarning("Publishing discovery to {Topic} failed: {ReasonCode} {Reason}", topic, result.ReasonCode, result.ReasonString);

        return result.IsSuccess;
    }

    public async Task ClearRetainedAsync(string topicFilter, TimeSpan collectWindow)
    {
        CheckConnected();

        var topics = new HashSet<string>();

        Task Collector(MqttApplicationMessageReceivedEventArgs eventArgs)
        {
            if (eventArgs.ApplicationMessage.Retain)
                topics.Add(eventArgs.ApplicationMessage.Topic);

            return Task.CompletedTask;
        }

        _mqttClient.ApplicationMessageReceivedAsync += Collector;

        try
        {
            await _mqttClient.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(topicFilter)
                .Build());

            await Task.Delay(collectWindow);

            await _mqttClient.UnsubscribeAsync(topicFilter);
        }
        finally
        {
            _mqttClient.ApplicationMessageReceivedAsync -= Collector;
        }

        foreach (var topic in topics)
        {
            await _mqttClient.PublishAsync(new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithRetainFlag()
                .Build());
        }

        Logger.LogInformation("Cleared {Count} retained message(s) under {Filter}", topics.Count, topicFilter);
    }

    public async Task<bool> SubscribeAsync(string topic, MqttMessageHandler messageHandler,
        MqttSubscribeFlags flags = MqttSubscribeFlags.None)
    {
        CheckConnected();

        // Claimed before subscribing, so two callers racing for one topic
        // cannot both get it.
        if (!_messageHandlers.TryAdd(topic, new Subscription(messageHandler, flags)))
        {
            Logger.LogWarning("{Topic} already has a handler — the second subscription is ignored", topic);
            return false;
        }

        try
        {
            await _mqttClient.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(topic)
                .Build());
        }
        catch
        {
            _messageHandlers.TryRemove(topic, out _);
            throw;
        }

        Logger.LogTrace("Subscribed to {Topic}", topic);

        return true;
    }

    private void CheckConnected()
    {
        if (_mqttClient is not { IsConnected: true })
            throw new InvalidOperationException("MQTT broker is not connected");
    }

    // Commands from Home Assistant. A handler that throws must not escape:
    // MQTTnet would swallow the exception without a trace, and the user would
    // only see a switch that springs back.
    private async Task HandleMessageReceived(MqttApplicationMessageReceivedEventArgs eventArgs)
    {
        string topic = eventArgs.ApplicationMessage.Topic;

        if (!_messageHandlers.TryGetValue(topic, out var subscription))
            return;

        string message = Encoding.UTF8.GetString(eventArgs.ApplicationMessage.Payload);

        var flags = subscription.Flags;
        var loggedPayload = flags.HasFlag(MqttSubscribeFlags.PrivatePayload)
            ? $"<{message.Length} chars>"
            : message;

        if (eventArgs.ApplicationMessage.Retain && flags.HasFlag(MqttSubscribeFlags.IgnoreRetained))
        {
            // The broker forwards the clearing publish back to us as an empty
            // live message; the handler is expected to ignore an empty payload.
            if (message.Length > 0)
            {
                Logger.LogWarning("Command {Topic}: retained message {Payload} ignored and cleared", topic, loggedPayload);
                await PublishAsync(topic, string.Empty, retain: true);
            }

            return;
        }

        Logger.LogDebug("Command {Topic}: {Payload}", topic, loggedPayload);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await subscription.Handler(topic, message);

            Logger.LogDebug("Command {Topic} handled in {Elapsed} ms", topic, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Command {Topic}: {Payload} failed after {Elapsed} ms", topic, loggedPayload, stopwatch.ElapsedMilliseconds);
        }
    }

    private sealed record Subscription(MqttMessageHandler Handler, MqttSubscribeFlags Flags);

    private Task HandleDisconnected(MqttClientDisconnectedEventArgs eventArgs)
    {
        var cancellation = _reconnectCancellation;

        // Nothing to do before the first successful connect or after an
        // intentional DisconnectAsync.
        if (cancellation == null || cancellation.IsCancellationRequested)
            return Task.CompletedTask;

        // Failed reconnect attempts fire this event too — one loop is enough.
        if (Interlocked.Exchange(ref _reconnecting, 1) == 1)
            return Task.CompletedTask;

        Logger.LogWarning(eventArgs.Exception, "MQTT connection lost ({Reason}) — reconnecting", eventArgs.Reason);
        ConnectionChanged?.Invoke(false);

        _ = ReconnectLoopAsync(cancellation.Token);

        return Task.CompletedTask;
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        var attempts = 0;

        try
        {
            var delay = InitialReconnectDelay;

            while (true)
            {
                await Task.Delay(delay, cancellationToken);

                attempts++;

                try
                {
                    var result = await _mqttClient.ConnectAsync(_options!, cancellationToken);

                    if (result.ResultCode == MqttClientConnectResultCode.Success)
                        break;

                    Logger.LogDebug("Reconnect attempt {Attempt} refused: {ResultCode}", attempts, result.ResultCode);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Broker still unreachable — retry with backoff.
                    Logger.LogDebug("Reconnect attempt {Attempt} failed: {Message}", attempts, ex.Message);
                }

                delay = delay >= MaxReconnectDelay ? MaxReconnectDelay : delay * 2;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            Interlocked.Exchange(ref _reconnecting, 0);
        }

        Logger.LogInformation("MQTT reconnected after {Attempts} attempt(s)", attempts);
        ConnectionChanged?.Invoke(true);

        // The session was clean, so the broker dropped our subscriptions.
        // If the connection breaks again mid-restore, the next loop simply
        // redoes this part.
        try
        {
            var topics = _messageHandlers.Keys.ToArray();

            foreach (var topic in topics)
            {
                await _mqttClient.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(topic)
                    .Build(), cancellationToken);
            }

            Logger.LogDebug("Restored {Count} subscription(s)", topics.Length);

            if (ReconnectedAsync != null)
                await ReconnectedAsync.Invoke();
        }
        catch (Exception ex)
        {
            // Restoration is retried by the loop the new disconnect spawns.
            Logger.LogWarning(ex, "Restoring the session after reconnect failed");
        }
    }
}

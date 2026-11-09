using HomeAssistant.Connector.Mqtt;
using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

public delegate Task NotifyMessageHandler(string message);

// A target for notify.send_message: the message goes to the command topic and
// nowhere else — there is no state, so the text is not kept by Home Assistant
// (recorder) nor by the broker. Meant for user content such as clipboard text.
public class NotifyEntity: BaseEntity
{
    public NotifyMessageHandler? MessageHandler { get; init; }

    public override MqttDiscoveryConfig GetDiscovery()
    {
        var config = CreateDiscovery<MqttNotifyDiscoveryConfig>();

        config.CommandTopic = GetCommandTopic();

        return config;
    }

    protected override async Task SubscribeControlTopicAsync()
    {
        await MqttClient.SubscribeAsync(GetCommandTopic(), HandleCommandAsync,
            MqttSubscribeFlags.IgnoreRetained | MqttSubscribeFlags.PrivatePayload);
    }

    private async Task HandleCommandAsync(string topic, string message)
    {
        // Empty is also what clearing a retained message looks like.
        if (MessageHandler != null && message.Length > 0)
            await MessageHandler(message);
    }
}

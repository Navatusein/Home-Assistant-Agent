using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

public delegate Task ButtonClickHandler();

public class ButtonEntity: BaseEntity
{
    private const string PayloadPress = "PRESS";

    public ButtonClickHandler? ClickHandler { get; set; }

    // Availability is the base one — the device's and the entity's own — so
    // the plugin can take a button offline (a disabled plugin, a client that
    // lost its connection) instead of leaving it pressable with nothing to
    // handle the press.
    public override MqttDiscoveryConfig GetDiscovery()
    {
        var config = CreateDiscovery<MqttButtonDiscoveryConfig>();

        config.PayloadPress = PayloadPress;
        config.CommandTopic = GetCommandTopic();

        return config;
    }

    protected override async Task SubscribeControlTopicAsync()
    {
        await MqttClient.SubscribeAsync(GetCommandTopic(), HandleCommandAsync);
    }

    private async Task HandleCommandAsync(string topic, string message)
    {
        if (ClickHandler != null && message == PayloadPress)
            await ClickHandler();
    }
}

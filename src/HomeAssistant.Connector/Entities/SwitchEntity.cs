using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

public delegate Task<bool> SwitchChangeStateHandler(bool newState);

public class SwitchEntity: BaseEntity
{
    private const string PayloadOn = "ON";
    private const string PayloadOff = "OFF";

    public bool DefaultState { get; init; }

    public SwitchChangeStateHandler? ChangeStateHandler { get; init; }

    public override MqttDiscoveryConfig GetDiscovery()
    {
        var config = CreateDiscovery<MqttSwitchDiscoveryConfig>();

        config.PayloadOn = PayloadOn;
        config.PayloadOff = PayloadOff;
        config.CommandTopic = GetCommandTopic();
        config.StateTopic = GetStateTopic();

        return config;
    }

    // Retained so Home Assistant picks the state up as soon as it subscribes
    // after processing the discovery config (see SensorEntity.SetStateAsync).
    public async Task<bool> SetStateAsync(bool state)
    {
        return await MqttClient.PublishAsync(GetStateTopic(), state ? PayloadOn : PayloadOff, retain: true);
    }

    protected override async Task PublishInitialStateAsync()
    {
        await SetStateAsync(DefaultState);
    }

    protected override async Task SubscribeControlTopicAsync()
    {
        await MqttClient.SubscribeAsync(GetCommandTopic(), HandleCommandAsync);
    }

    // The handler answers with the state that actually took effect, which is
    // what gets published — a refused change springs back in Home Assistant.
    private async Task HandleCommandAsync(string topic, string message)
    {
        bool newState = message == PayloadOn;

        if (ChangeStateHandler != null)
            newState = await ChangeStateHandler(newState);

        await SetStateAsync(newState);
    }
}

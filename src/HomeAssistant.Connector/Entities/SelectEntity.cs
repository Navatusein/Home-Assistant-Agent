using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

public delegate Task<string> SelectChangeStateHandler(string newOption);

public class SelectEntity: BaseEntity
{
    // Settable because some option lists are only known once the app they
    // mirror is running (OBS scenes). Change them on the registered entity and
    // call HomeAssistantClient.RefreshDiscoveryAsync — replacing the entity
    // instead would leave the command subscription bound to the old one.
    public required string[] Options { get; set; }

    // Optional: an entity whose options are not known up front has no
    // meaningful state to announce either.
    public string? DefaultState { get; init; }

    public SelectChangeStateHandler? ChangeStateHandler { get; init; }

    public override MqttDiscoveryConfig GetDiscovery()
    {
        var config = CreateDiscovery<MqttSelectDiscoveryConfig>();

        config.Options = [..Options];
        config.CommandTopic = GetCommandTopic();
        config.StateTopic = GetStateTopic();

        return config;
    }

    // Retained so Home Assistant picks the state up as soon as it subscribes
    // after processing the discovery config (see SensorEntity.SetStateAsync).
    public async Task<bool> SetStateAsync(string state)
    {
        return await MqttClient.PublishAsync(GetStateTopic(), state, retain: true);
    }

    protected override async Task PublishInitialStateAsync()
    {
        // Without a default the retained state from the last run is left in
        // place — Home Assistant rejects a state that is not an option anyway.
        if (!string.IsNullOrEmpty(DefaultState))
            await SetStateAsync(DefaultState);
    }

    protected override async Task SubscribeControlTopicAsync()
    {
        await MqttClient.SubscribeAsync(GetCommandTopic(), HandleCommandAsync);
    }

    private async Task HandleCommandAsync(string topic, string message)
    {
        if (!Options.Contains(message))
            return;

        var newOption = message;

        if (ChangeStateHandler != null)
            newOption = await ChangeStateHandler(message);

        await SetStateAsync(newOption);
    }
}

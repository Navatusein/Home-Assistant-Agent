using System.Globalization;
using HomeAssistantDiscoveryNet;

namespace HomeAssistant.Connector.Entities;

public delegate Task<double> NumberChangeStateHandler(double newValue);

public class NumberEntity: BaseEntity
{
    public double DefaultState { get; init; }
    public double Min { get; init; }
    public double Max { get; init; } = 100;
    public double Step { get; init; } = 1;
    public string? UnitOfMeasurement { get; init; }

    public NumberChangeStateHandler? ChangeStateHandler { get; init; }

    public override MqttDiscoveryConfig GetDiscovery()
    {
        var config = CreateDiscovery<MqttNumberDiscoveryConfig>();

        config.Min = Min;
        config.Max = Max;
        config.Step = Step;
        config.UnitOfMeasurement = UnitOfMeasurement;
        config.CommandTopic = GetCommandTopic();
        config.StateTopic = GetStateTopic();

        return config;
    }

    // Retained so Home Assistant picks the state up as soon as it subscribes
    // after processing the discovery config (see SensorEntity.SetStateAsync).
    public async Task<bool> SetStateAsync(double value)
    {
        return await MqttClient.PublishAsync(GetStateTopic(), value.ToString(CultureInfo.InvariantCulture), retain: true);
    }

    protected override async Task PublishInitialStateAsync()
    {
        await SetStateAsync(DefaultState);
    }

    protected override async Task SubscribeControlTopicAsync()
    {
        await MqttClient.SubscribeAsync(GetCommandTopic(), HandleCommandAsync);
    }

    private async Task HandleCommandAsync(string topic, string message)
    {
        if (!double.TryParse(message, NumberStyles.Float, CultureInfo.InvariantCulture, out var newValue))
            return;

        if (ChangeStateHandler != null)
            newValue = await ChangeStateHandler(newValue);

        await SetStateAsync(newValue);
    }
}

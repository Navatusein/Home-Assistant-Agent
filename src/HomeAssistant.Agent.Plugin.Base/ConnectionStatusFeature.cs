using HomeAssistant.Connector.Entities;

namespace HomeAssistant.Agent.Plugin.Base;

// The "is the agent connected to <app>" sensor that plugins talking to a
// running app (Discord, OBS, Teams) share. Unlike their other entities it
// stays available while the connection is down — reporting that is its job.
// Driven by BasePlugin.PublishConnectionStateAsync.
public sealed class ConnectionStatusFeature : PluginFeature
{
    // pluginPrefix names the ids: "obs" -> obs_status, obs/status.
    public ConnectionStatusFeature(string pluginPrefix, string appName)
        : base("status", "Connection Status", $"Whether the agent is connected to {appName}.", PluginHostMode.Desktop)
    {
        Sensor = new BinarySensorEntity
        {
            Name = $"{appName} Plugin Status",
            UniqueId = $"{pluginPrefix}_status",
            BaseTopic = $"{pluginPrefix}/status",
            DeviceClass = "connectivity",
            DefaultState = false,
        };
    }

    public BinarySensorEntity Sensor { get; }

    public override IEntity[] GetEntities() => [Sensor];
}

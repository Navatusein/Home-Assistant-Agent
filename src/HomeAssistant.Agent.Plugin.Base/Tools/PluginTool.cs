namespace HomeAssistant.Agent.Plugin.Base.Tools;

public delegate Task<string> PluginToolHandler(IReadOnlyDictionary<string, string> parameters);

public sealed class PluginTool
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required PluginToolHandler Handler { get; init; }
}

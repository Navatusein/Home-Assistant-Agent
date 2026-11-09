namespace HomeAssistant.Agent.Core;

public static class AgentInfo
{
    // The agent's version (Directory.Build.props, or the release tag).
    public static Version Version { get; } = typeof(AgentInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
}

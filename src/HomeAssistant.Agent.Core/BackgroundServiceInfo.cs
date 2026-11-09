namespace HomeAssistant.Agent.Core;

// The agent's background service — the Windows service, or the launchd daemon
// on macOS — as the desktop app, the service itself and an installer all need
// to name it.
public static class BackgroundServiceInfo
{
    // The registration id with the Windows service manager. Kept from before
    // the project rename: changing it would orphan an installed service (it
    // would need an uninstall under the old name and a new install).
    public const string ServiceName = "HomeAssistantAgentService";

    // The launchd label of the daemon on macOS, and its plist.
    public const string LaunchdLabel = "com.navatusein.homeassistant-agent.service";

    public static string LaunchDaemonPlist => $"/Library/LaunchDaemons/{LaunchdLabel}.plist";

    // The service executable, next to the desktop app.
    public static string ExecutableName => OperatingSystem.IsWindows()
        ? "HomeAssistant.Agent.Service.exe"
        : "HomeAssistant.Agent.Service";

    public static string ExecutablePath => Path.Combine(AgentPaths.InstallDirectory, ExecutableName);
}

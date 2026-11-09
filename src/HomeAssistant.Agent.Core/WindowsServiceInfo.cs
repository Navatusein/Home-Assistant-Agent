namespace HomeAssistant.Agent.Core;

// The Windows service as the desktop app, the service itself and an installer
// all need to name it.
public static class WindowsServiceInfo
{
    // The registration id with the service manager. Kept from before the
    // project rename: changing it would orphan an installed service (it would
    // need an uninstall under the old name and a new install).
    public const string ServiceName = "HomeAssistantAgentService";

    public const string ExecutableName = "HomeAssistant.Agent.Service.exe";
}

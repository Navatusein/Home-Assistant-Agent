using System.Runtime.Versioning;
using Microsoft.Win32;

namespace HomeAssistant.Agent.Core.Platform.Windows;

// The current-user Run entry. Desktop-only: the service has its own startup
// in the service manager.
[SupportedOSPlatform("windows")]
public sealed class WindowsAutoStart : IAutoStart
{
    // The Run value name predates the project rename. Keeping it means an
    // existing entry is overwritten with the new executable path instead of
    // leaving a second, dangling one behind.
    private const string RunValueName = "HomeAssistantAgent";

    public void Apply(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");

        if (enabled)
            runKey.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        else
            runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
    }
}

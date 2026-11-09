using System.Diagnostics;
using System.Runtime.Versioning;

namespace HomeAssistant.Agent.Core.Platform.Windows;

// Explorer.
[SupportedOSPlatform("windows")]
public sealed class WindowsShell : IShell
{
    public void OpenFolder(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public void RevealFile(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
}

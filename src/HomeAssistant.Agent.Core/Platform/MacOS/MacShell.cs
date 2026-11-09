using System.Diagnostics;
using System.Runtime.Versioning;

namespace HomeAssistant.Agent.Core.Platform.MacOS;

// Finder, through open(1).
[SupportedOSPlatform("macos")]
public sealed class MacShell : IShell
{
    public void OpenFolder(string path) => Open(path);

    public void RevealFile(string path) => Open("-R", path);

    private static void Open(params string[] arguments) =>
        Process.Start(new ProcessStartInfo("/usr/bin/open", arguments) { UseShellExecute = false })?.Dispose();
}

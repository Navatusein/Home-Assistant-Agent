using System.Runtime.InteropServices;

namespace HomeAssistant.Agent.Core.Plugins;

// The runtime identifiers a plugin's platform-specific dependencies are packed
// under (see EmbedPluginDependencies), for the running system: most specific
// first, as NuGet picks them — "win-x64", "win"; "osx-arm64", "osx", "unix".
internal static class PluginRuntime
{
    public static IReadOnlyList<string> Identifiers { get; } = Create();

    private static string[] Create()
    {
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        if (OperatingSystem.IsWindows())
            return [$"win-{architecture}", "win"];

        if (OperatingSystem.IsMacOS())
            return [$"osx-{architecture}", "osx", "unix"];

        return [$"linux-{architecture}", "linux", "unix"];
    }
}

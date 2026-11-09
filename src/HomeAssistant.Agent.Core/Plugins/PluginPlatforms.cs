using System.Reflection;
using System.Runtime.Versioning;

namespace HomeAssistant.Agent.Core.Plugins;

// Which operating systems a plugin runs on. A plugin built for an OS-specific
// target framework (net10.0-windows…) carries [SupportedOSPlatform("Windows…")]
// on its assembly — the SDK adds it — and needs that system: the Windows plugin
// is built on WinForms and WinRT. A plain net10.0 plugin carries none and runs
// anywhere.
public static class PluginPlatforms
{
    // "windows", "macos", ... — lower case, without the version. Empty: any.
    public static IReadOnlyList<string> Of(Assembly assembly) =>
        assembly.GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName == typeof(SupportedOSPlatformAttribute).FullName)
            .Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string)
            .OfType<string>()
            .Select(Normalize)
            .Distinct()
            .ToArray();

    public static bool IsSupported(IReadOnlyCollection<string>? platforms) =>
        platforms == null || platforms.Count == 0 || platforms.Any(OperatingSystem.IsOSPlatform);

    // "Windows10.0.19041.0" -> "windows"; "macOS" -> "macos".
    private static string Normalize(string platform)
    {
        var end = 0;

        while (end < platform.Length && char.IsLetter(platform[end]))
            end++;

        return platform[..end].ToLowerInvariant();
    }
}

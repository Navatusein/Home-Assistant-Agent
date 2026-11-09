using System.Reflection;

namespace HomeAssistant.Agent.Plugin.Base;

// What a plugin says about itself: name, description, author, links, icon.
// Declared in the plugin's project file (see src/Directory.Build.targets) and
// read from the assembly's metadata — never from plugin code, so it can be
// shown for a plugin that failed to start, and read from a dll that is not
// installed at all.
public sealed class PluginInfo
{
    public const string MetadataPrefix = "AgentPlugin.";
    public const string IconResource = "AgentPlugin.Icon";

    private readonly Assembly _assembly;

    private PluginInfo(Assembly assembly)
    {
        _assembly = assembly;

        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal))
            .GroupBy(attribute => attribute.Key[MetadataPrefix.Length..])
            .ToDictionary(group => group.Key, group => group.Last().Value, StringComparer.OrdinalIgnoreCase);

        Name = NullIfEmpty(metadata.GetValueOrDefault("Name"));
        Description = NullIfEmpty(metadata.GetValueOrDefault("Description"));
        Author = NullIfEmpty(metadata.GetValueOrDefault("Author"));
        Homepage = ParseWebLink(metadata.GetValueOrDefault("Homepage"));
        Repository = ParseWebLink(metadata.GetValueOrDefault("Repository"));
        Version = assembly.GetName().Version?.ToString(3);
        HasIcon = assembly.GetManifestResourceNames().Contains(IconResource);
    }

    public string? Name { get; }

    public string? Description { get; }

    public string? Author { get; }

    public Uri? Homepage { get; }

    public Uri? Repository { get; }

    public string? Version { get; }

    public bool HasIcon { get; }

    public static PluginInfo FromAssembly(Assembly assembly) => new(assembly);

    // The icon as an image stream (PNG), or null. The caller disposes it.
    public Stream? OpenIcon() => HasIcon ? _assembly.GetManifestResourceStream(IconResource) : null;

    // Only web links: the host opens them with the shell, and a plugin from an
    // unknown source must not be able to turn "Homepage" into a local path or
    // an executable.
    private static Uri? ParseWebLink(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                ? uri
                : null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

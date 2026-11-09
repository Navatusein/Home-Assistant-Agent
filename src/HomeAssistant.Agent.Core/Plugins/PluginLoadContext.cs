using System.Reflection;
using System.Runtime.Loader;

namespace HomeAssistant.Agent.Core.Plugins;

// Isolates a plugin's dependencies from the host and from other plugins,
// while keeping assemblies shipped with the host shared between everyone.
internal class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;
    private readonly string _pluginAssemblyPath;
    private readonly string _pluginDirectory;
    private Assembly? _pluginAssembly;

    public PluginLoadContext(string pluginAssemblyPath)
        : base(Path.GetFileNameWithoutExtension(pluginAssemblyPath))
    {
        _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
        _pluginAssemblyPath = pluginAssemblyPath;
        _pluginDirectory = Path.GetDirectoryName(pluginAssemblyPath)!;
    }

    // What the default context resolves: the process's trusted platform
    // assemblies (its .deps.json plus the shared frameworks).
    private static readonly HashSet<string> HostAssemblies = LoadHostAssemblies();

    private static HashSet<string> LoadHostAssemblies()
    {
        var list = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;

        return list
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // The plugin's main assembly once it has been loaded into this context;
    // single-file plugins carry their dependencies inside it as resources.
    private Assembly? PluginAssembly => _pluginAssembly ??= Assemblies.FirstOrDefault(a =>
        string.Equals(a.Location, _pluginAssemblyPath, StringComparison.OrdinalIgnoreCase));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == null)
            return null;

        // Assemblies the host ships (HomeAssistant.Agent.Plugin.Base,
        // HomeAssistant.Connector, ...) must load in the default context,
        // otherwise the plugin's BasePlugin would be a different .NET type
        // than the one the host casts to.
        //
        // "Ships" means this process can load it, not that a file of that name
        // sits in the folder: the app and the service share one folder but
        // not their dependencies. The app brings Microsoft.Windows.SDK.NET, and
        // the service, seeing the file, would refuse the plugin's own copy and
        // then fail to load it.
        if (HostAssemblies.Contains(assemblyName.Name))
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName)
            ?? Path.Combine(_pluginDirectory, assemblyName.Name + ".dll");

        if (File.Exists(path))
            return LoadFromAssemblyPath(path);

        return LoadFromEmbeddedResource(assemblyName.Name);
    }

    // Dependencies embedded by the EmbedPluginDependencies build target under
    // the "EmbeddedAssemblies/<name>.dll" logical name.
    private Assembly? LoadFromEmbeddedResource(string assemblyName)
    {
        using var stream = PluginAssembly?.GetManifestResourceStream($"EmbeddedAssemblies/{assemblyName}.dll");
        return stream != null ? LoadFromStream(stream) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}

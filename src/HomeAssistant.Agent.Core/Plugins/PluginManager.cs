using System.Reflection;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Core.Storage;
using HomeAssistant.Agent.Plugin.Base;
using HomeAssistant.Connector;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Core.Plugins;

public record PluginLoadError(string Source, string Message);

// A plugin left out on purpose: built for another operating system.
public record SkippedPlugin(string Source, IReadOnlyList<string> Platforms);

// Loads plugins from dll files instead of project references. A plugin is a
// single dll with its dependencies embedded as resources; a subdirectory
// holding the dll together with loose dependency files is also supported (the
// main assembly is the dll named after the directory).
//
// Plugins come from one folder or several (AgentPaths.PluginDirectories: the
// catalog-installed ones, then the bundled ones). A plugin found in an earlier
// folder hides the same file in a later one, so a catalog update of a bundled
// plugin wins over its copy in the app bundle.
//
// Every plugin it creates is initialized with its PluginContext straight away,
// so the rest of the host never sees a plugin without one.
public class PluginManager
{
    // The plugin API: the host-shipped assemblies a plugin compiles against.
    // A plugin built for a different major version — or a newer minor one —
    // would fail later with a TypeLoadException or a MissingMethodException in
    // the middle of running; checking the references up front turns that into
    // a clear load error on the Dashboard.
    private static readonly Assembly[] ContractAssemblies =
        [typeof(BasePlugin).Assembly, typeof(HomeAssistantClient).Assembly];

    private readonly List<BasePlugin> _plugins = [];
    private readonly List<PluginLoadError> _errors = [];
    private readonly List<SkippedPlugin> _skipped = [];

    private readonly ILoggerFactory _loggerFactory;
    private readonly PersistentStore _store;
    private readonly string _agentVersion;

    public PluginHostMode HostMode { get; }

    // Where plugins load from, in order; ImportPlugin copies into the first.
    public IReadOnlyList<string> PluginDirectories { get; }

    public string PluginsDirectory => PluginDirectories[0];

    public IReadOnlyList<BasePlugin> Plugins => _plugins;

    public IReadOnlyList<PluginLoadError> Errors => _errors;

    // Built for another operating system (the Windows plugin on a Mac): not an
    // error, just not for this machine.
    public IReadOnlyList<SkippedPlugin> Skipped => _skipped;

    public PluginManager(PluginHostMode hostMode, ILoggerFactory loggerFactory, PersistentStore store,
        string? pluginsDirectory = null)
        : this(hostMode, loggerFactory, store, pluginsDirectory == null ? null : [pluginsDirectory])
    {
    }

    public PluginManager(PluginHostMode hostMode, ILoggerFactory loggerFactory, PersistentStore store,
        IReadOnlyList<string>? pluginDirectories)
    {
        HostMode = hostMode;
        _loggerFactory = loggerFactory;
        _store = store;
        _agentVersion = typeof(PluginManager).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        PluginDirectories = pluginDirectories is { Count: > 0 } ? pluginDirectories : AgentPaths.PluginDirectories;
    }

    // "2.0" — the plugin API this host provides.
    public static Version ApiVersion => typeof(BasePlugin).Assembly.GetName().Version!;

    public void LoadPlugins()
    {
        // By name, so an earlier folder hides the same plugin in a later one.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pluginsDirectory in PluginDirectories)
        {
            // The installed folder may not exist yet (no plugin from a catalog
            // so far) and may not be ours to create (root's on macOS).
            if (!Directory.Exists(pluginsDirectory))
                continue;

            LoadFrom(pluginsDirectory, seen);
        }
    }

    private void LoadFrom(string pluginsDirectory, HashSet<string> seen)
    {
        // Single-file plugins: dependencies are embedded in the dll.
        foreach (var dll in Directory.GetFiles(pluginsDirectory, "*.dll"))
        {
            if (seen.Add(Path.GetFileNameWithoutExtension(dll)))
                LoadPluginAssembly(dll);
        }

        // Directory plugins: dependencies lie next to the main dll.
        foreach (var directory in Directory.GetDirectories(pluginsDirectory))
        {
            if (!seen.Add(Path.GetFileName(directory)))
                continue;

            var assemblyPath = FindPluginAssembly(directory);

            if (assemblyPath == null)
            {
                _errors.Add(new PluginLoadError(directory,
                    "No plugin assembly found. Name the main dll after its directory."));
                continue;
            }

            LoadPluginAssembly(assemblyPath);
        }
    }

    // Imports a plugin from an arbitrary location: copies the dll (which
    // carries its dependencies as embedded resources) into PluginsDirectory
    // and loads it. Returns the plugins found in the imported assembly.
    public BasePlugin[] ImportPlugin(string dllPath)
    {
        var sourcePath = Path.GetFullPath(dllPath);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Plugin assembly not found.", sourcePath);

        var pluginName = Path.GetFileNameWithoutExtension(sourcePath);
        var targetPath = Path.Combine(PluginsDirectory, pluginName + ".dll");

        if (File.Exists(targetPath) || Directory.Exists(Path.Combine(PluginsDirectory, pluginName)))
            throw new InvalidOperationException($"Plugin '{pluginName}' is already imported.");

        Directory.CreateDirectory(PluginsDirectory);
        File.Copy(sourcePath, targetPath);

        var countBefore = _plugins.Count;
        LoadPluginAssembly(targetPath);
        return _plugins.Skip(countBefore).ToArray();
    }

    private void LoadPluginAssembly(string assemblyPath)
    {
        try
        {
            var context = new PluginLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);

            // Before anything else: a plugin for another OS fails in confusing
            // ways (a missing WinForms) the moment its types are touched.
            var platforms = PluginPlatforms.Of(assembly);

            if (!PluginPlatforms.IsSupported(platforms))
            {
                _skipped.Add(new SkippedPlugin(assemblyPath, platforms));
                return;
            }

            // Reading the references touches no plugin type, so an
            // incompatible plugin is rejected before anything can fail to load.
            var incompatibility = CheckApiCompatibility(assembly);

            if (incompatibility != null)
            {
                _errors.Add(new PluginLoadError(assemblyPath, incompatibility));
                return;
            }

            var pluginTypes = assembly.GetTypes()
                .Where(t => !t.IsAbstract && typeof(BasePlugin).IsAssignableFrom(t))
                .ToArray();

            if (pluginTypes.Length == 0)
            {
                _errors.Add(new PluginLoadError(assemblyPath,
                    $"Assembly contains no {nameof(BasePlugin)} implementations."));
                return;
            }

            foreach (var type in pluginTypes)
            {
                var plugin = (BasePlugin)Activator.CreateInstance(type)!;

                if (!TryAdd(plugin, out var error))
                    _errors.Add(new PluginLoadError(assemblyPath, error));
            }
        }
        catch (Exception ex)
        {
            _errors.Add(new PluginLoadError(assemblyPath, DescribeLoadError(ex)));
        }
    }

    // Adds a plugin instance the host created itself (one compiled into the
    // host, or a test double) — initialized exactly like a loaded one.
    public void Register(BasePlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        if (!TryAdd(plugin, out var error))
            throw new InvalidOperationException(error);
    }

    private bool TryAdd(BasePlugin plugin, out string error)
    {
        if (_plugins.Any(p => p.PluginCode == plugin.PluginCode))
        {
            error = $"Duplicate plugin code '{plugin.PluginCode}' — skipped.";
            return false;
        }

        // Tags everything logged from the plugin's assembly with its code —
        // "discord-plugin/DiscordClient" in the log, and the key for the
        // plugin's own log level. Registered before Initialize, so whatever the
        // plugin logs from there on is tagged already.
        SourceEnricher.RegisterPlugin(plugin.GetType().Assembly.GetName().Name!, plugin.PluginCode);

        plugin.Initialize(new PluginContext
        {
            HostMode = HostMode,
            LoggerFactory = _loggerFactory,
            Storage = new PluginStorage(_store, plugin.PluginCode),
            AgentVersion = _agentVersion,
        });

        _plugins.Add(plugin);

        error = string.Empty;
        return true;
    }

    // Same major version, and a minor version the host has caught up with:
    // a plugin built for API 2.0 runs on a 2.3 host, not the other way round.
    private static string? CheckApiCompatibility(Assembly plugin)
    {
        var references = plugin.GetReferencedAssemblies();

        foreach (var contract in ContractAssemblies)
        {
            var provided = contract.GetName();
            var required = references.FirstOrDefault(reference => reference.Name == provided.Name)?.Version;

            // Not referenced at all: the plugin does not use that part of the
            // API (or is not a plugin — GetTypes reports that next).
            if (required == null || provided.Version == null)
                continue;

            if (required.Major != provided.Version.Major || required.Minor > provided.Version.Minor)
            {
                return $"Built for {provided.Name} {required.ToString(2)}, but this agent provides "
                    + $"{provided.Version.ToString(2)}. Update the "
                    + (required > provided.Version ? "agent." : "plugin.");
            }
        }

        return null;
    }

    // Reflection wraps the real failure: Activator.CreateInstance in
    // TargetInvocationException ("Exception has been thrown by the target of
    // an invocation"), Assembly.GetTypes in ReflectionTypeLoadException.
    // Report the underlying cause instead of the wrapper's generic message.
    private static string DescribeLoadError(Exception ex)
    {
        if (ex is System.Reflection.ReflectionTypeLoadException typeLoad)
        {
            var reasons = typeLoad.LoaderExceptions
                .Where(e => e != null)
                .Select(e => e!.Message)
                .Distinct();

            return string.Join(" ", reasons);
        }

        return ex.GetBaseException().Message;
    }

    private static string? FindPluginAssembly(string directory)
    {
        var byDirectoryName = Path.Combine(directory, Path.GetFileName(directory) + ".dll");

        if (File.Exists(byDirectoryName))
            return byDirectoryName;

        var dlls = Directory.GetFiles(directory, "*.dll");
        return dlls.Length == 1 ? dlls[0] : null;
    }
}

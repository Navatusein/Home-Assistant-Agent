using System.Security.Cryptography;

namespace HomeAssistant.Agent.Core.Plugins;

// Native libraries a single-file plugin carries for the running system, as
// the EmbedPluginDependencies build target packs them:
// "EmbeddedNative/<rid>/<file>". The system loads a native library only from
// a file, so they are unpacked on first use, all together — one may depend on
// another next to it.
//
// The cache is per user (LocalApplicationData), never the shared data
// directory: the service runs as SYSTEM or root and must not load a library
// that any user could have put there. A folder is named after the hash of what
// it holds and every file is checked against it before use, so a changed or
// half-written file is unpacked again.
internal sealed class EmbeddedNativeLibraries
{
    private const string Prefix = "EmbeddedNative/";

    private readonly Func<string, Stream?> _open;
    private readonly Dictionary<string, string> _resources;
    private readonly string _cacheDirectory;
    private readonly object _lock = new();
    private string? _directory;

    // runtimeIdentifiers: the running system's, most specific first
    // (PluginRuntime.Identifiers); the libraries of the first one the plugin
    // carries any for are used.
    public EmbeddedNativeLibraries(IReadOnlyCollection<string> resourceNames, Func<string, Stream?> open,
        string pluginName, IEnumerable<string> runtimeIdentifiers, string cacheRoot)
    {
        _open = open;
        _cacheDirectory = Path.Combine(cacheRoot, pluginName);
        _resources = new Dictionary<string, string>(FileNameComparer);

        foreach (var runtimeIdentifier in runtimeIdentifiers)
        {
            var prefix = $"{Prefix}{runtimeIdentifier}/";

            // File name → resource name.
            foreach (var name in resourceNames.Where(name => name.StartsWith(prefix, StringComparison.Ordinal)))
                _resources[name[prefix.Length..]] = name;

            if (_resources.Count > 0)
                break;
        }
    }

    public static string DefaultCacheRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create),
        AgentPaths.DataFolderName, "NativeLibraries");

    private static StringComparer FileNameComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // The path of the library a DllImport names ("libsodium", "libsodium.dll",
    // "sodium"), or null when the plugin carries no such library.
    public string? Resolve(string libraryName)
    {
        var fileName = Candidates(libraryName).FirstOrDefault(_resources.ContainsKey);

        if (fileName == null)
            return null;

        lock (_lock)
        {
            _directory ??= Unpack();
        }

        return Path.Combine(_directory, fileName);
    }

    // The names the system itself would try for a DllImport.
    private static IEnumerable<string> Candidates(string libraryName)
    {
        yield return libraryName;

        var extensions = new[] { ".dll", ".dylib", ".so" };

        if (extensions.Any(extension => libraryName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            yield break;

        foreach (var extension in extensions)
        {
            yield return libraryName + extension;

            if (!libraryName.StartsWith("lib", StringComparison.Ordinal))
                yield return "lib" + libraryName + extension;
        }
    }

    private string Unpack()
    {
        var files = _resources.ToDictionary(pair => pair.Key, pair => Read(pair.Value), FileNameComparer);
        var directory = Path.Combine(_cacheDirectory, Hash(files));

        Directory.CreateDirectory(directory);

        foreach (var (fileName, content) in files)
        {
            var path = Path.Combine(directory, fileName);

            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
                continue;

            // Next to the target and then moved over it: a process loading the
            // library at the same time never sees a half-written file.
            var temporary = $"{path}.{Guid.NewGuid():N}.tmp";

            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }

        return directory;
    }

    private byte[] Read(string resourceName)
    {
        using var stream = _open(resourceName)
            ?? throw new InvalidOperationException($"Embedded native library {resourceName} cannot be read.");
        using var buffer = new MemoryStream();

        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string Hash(IReadOnlyDictionary<string, byte[]> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        foreach (var (fileName, content) in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(fileName));
            hash.AppendData(content);
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..16].ToLowerInvariant();
    }
}

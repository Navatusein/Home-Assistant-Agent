// Prints the catalog entry of a built plugin as JSON:
//
//   PluginManifest <plugin.dll> [--url <download url>] [--release-notes <url>]
//                  [--icon-out <file.png>] [--icon-url <url>] [--out <entry.json>]
//
// The plugin is loaded the way the agent loads it (PluginManager: its own load
// context, embedded dependencies, the plugin API check), so a dll the agent
// would reject fails here, in the release, instead of on users' machines.
// Its code comes from running it — the release builds the author's own code.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Storage;
using HomeAssistant.Agent.Plugin.Base;
using Microsoft.Extensions.Logging.Abstractions;

if (args.Length == 0 || args[0].StartsWith("--"))
{
    Console.Error.WriteLine("Usage: PluginManifest <plugin.dll> [--url <url>] [--release-notes <url>] [--icon-out <file>] [--icon-url <url>] [--out <file>]");
    return 2;
}

var dllPath = Path.GetFullPath(args[0]);
var options = ParseOptions(args.Skip(1).ToArray());

if (!File.Exists(dllPath))
{
    Console.Error.WriteLine($"Not found: {dllPath}");
    return 2;
}

// A directory with nothing but this plugin: PluginManager loads every dll it
// finds.
var work = Directory.CreateTempSubdirectory("plugin-manifest-");

try
{
    var pluginPath = Path.Combine(work.FullName, Path.GetFileName(dllPath));
    File.Copy(dllPath, pluginPath);

    var manager = new PluginManager(PluginHostMode.Desktop, NullLoggerFactory.Instance,
        PersistentStore.Load(Path.Combine(work.FullName, "store.json")), work.FullName);

    manager.LoadPlugins();

    foreach (var error in manager.Errors)
        Console.Error.WriteLine($"error: {error.Message}");

    if (manager.Errors.Count > 0)
        return 1;

    if (manager.Plugins.Count != 1)
    {
        Console.Error.WriteLine($"error: a catalog entry is one plugin per dll, found {manager.Plugins.Count}.");
        return 1;
    }

    var plugin = manager.Plugins[0];
    var info = plugin.Info;
    var assembly = plugin.GetType().Assembly;

    var apiVersion = assembly.GetReferencedAssemblies()
        .FirstOrDefault(reference => reference.Name == typeof(BasePlugin).Assembly.GetName().Name)?.Version;

    if (options.TryGetValue("icon-out", out var iconOut))
    {
        using var icon = info.OpenIcon();

        if (icon != null)
        {
            await using var file = File.Create(iconOut);
            await icon.CopyToAsync(file);
        }
        else
        {
            options.Remove("icon-url");
        }
    }

    var entry = new CatalogPlugin
    {
        Code = plugin.PluginCode,
        Name = plugin.PluginName,
        Description = info.Description,
        Author = info.Author,
        Version = info.Version ?? "0.0.0",
        ApiVersion = apiVersion?.ToString(2) ?? "0.0",
        Url = options.GetValueOrDefault("url"),
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(dllPath))),
        Icon = options.GetValueOrDefault("icon-url"),
        ReleaseNotes = options.GetValueOrDefault("release-notes"),
        Homepage = info.Homepage?.AbsoluteUri,
        Repository = info.Repository?.AbsoluteUri,
    };

    var json = JsonSerializer.Serialize(entry, JsonOptions.Default.CatalogPlugin);

    if (options.TryGetValue("out", out var outPath))
        await File.WriteAllTextAsync(outPath, json);

    Console.WriteLine(json);
    return 0;
}
finally
{
    // The plugin's assembly stays loaded in this process; its copy may not be
    // deletable until exit, which is fine for a temp folder.
    try { work.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    for (var i = 0; i + 1 < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--"))
            throw new ArgumentException($"Unexpected argument '{args[i]}'.");

        options[args[i][2..]] = args[i + 1];
    }

    return options;
}

// One entry of index.json's "plugins" list (see docs/PLAN-refactor.md).
internal sealed record CatalogPlugin
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Author { get; init; }
    public required string Version { get; init; }
    public required string ApiVersion { get; init; }
    public string? Url { get; init; }
    public required string Sha256 { get; init; }
    public string? Icon { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? Homepage { get; init; }
    public string? Repository { get; init; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CatalogPlugin))]
internal sealed partial class JsonOptions : JsonSerializerContext;

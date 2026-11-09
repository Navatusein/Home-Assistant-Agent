using System.Text.Json;
using System.Text.Json.Serialization;

namespace HomeAssistant.Agent.Core.Updates;

// A plugin catalog: index.json in the "index" release of a repository, written
// by the release workflows (build/Update-Index.ps1, tools/PluginManifest). The
// official one also carries the agent's own latest release.
public sealed record CatalogIndex
{
    public int Schema { get; init; } = 1;

    public CatalogAgent? Agent { get; init; }

    public IReadOnlyList<CatalogPlugin> Plugins { get; init; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static CatalogIndex Parse(string json) =>
        JsonSerializer.Deserialize<CatalogIndex>(json, JsonOptions)
        ?? throw new JsonException("The catalog is empty.");
}

public sealed record CatalogAgent
{
    public required string Version { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public string? ReleaseNotes { get; init; }
}

public sealed record CatalogPlugin
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Author { get; init; }
    public required string Version { get; init; }

    // major.minor of the plugin API the plugin was built against.
    public required string ApiVersion { get; init; }

    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public string? Icon { get; init; }
    public string? ReleaseNotes { get; init; }
    public string? Homepage { get; init; }
    public string? Repository { get; init; }

    // The operating systems it runs on ("windows"); missing or empty: any.
    // Written by tools/PluginManifest from the dll (see PluginPlatforms).
    public IReadOnlyList<string>? Platforms { get; init; }

    [JsonIgnore]
    public bool IsSupportedHere => Plugins.PluginPlatforms.IsSupported(Platforms);

    // The dll's name in the Plugins folder: the last segment of its URL.
    [JsonIgnore]
    public string FileName => Uri.UnescapeDataString(new Uri(Url).Segments[^1]);
}

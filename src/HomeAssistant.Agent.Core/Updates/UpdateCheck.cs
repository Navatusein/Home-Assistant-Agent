namespace HomeAssistant.Agent.Core.Updates;

public sealed record CatalogSource(string Url, bool IsOfficial)
{
    // "github.com/owner/repo" — what the UI shows instead of the full URL.
    public string DisplayName
    {
        get
        {
            if (IsOfficial)
                return "Official";

            return Uri.TryCreate(Url, UriKind.Absolute, out var uri)
                ? string.Join('/', new[] { uri.Host }.Concat(uri.Segments.Skip(1).Take(2).Select(s => s.Trim('/'))))
                : Url;
        }
    }
}

// One catalog as fetched: its index, or why there is none.
public sealed record SourceResult(CatalogSource Source, CatalogIndex? Index, string? Error);

public enum PluginOfferState
{
    // Not installed; can be.
    Available,

    // Installed, and the catalog has a newer version.
    UpdateAvailable,

    // Installed and current.
    UpToDate,

    // Built for a newer plugin API than this agent provides.
    NeedsNewerAgent,
}

public sealed record PluginOffer(CatalogPlugin Plugin, CatalogSource Source, string? InstalledVersion, PluginOfferState State);

// Sources are the plugin catalogs; AgentIndex the agent's own index, read for
// its update only (null when it was not read).
public sealed record UpdateCheckResult(
    CatalogAgent? AgentUpdate,
    IReadOnlyList<PluginOffer> Plugins,
    IReadOnlyList<SourceResult> Sources,
    DateTimeOffset CheckedAt,
    SourceResult? AgentIndex = null)
{
    public static UpdateCheckResult Empty { get; } = new(null, [], [], DateTimeOffset.MinValue);

    public PluginOffer? FindPlugin(string code) =>
        Plugins.FirstOrDefault(offer => string.Equals(offer.Plugin.Code, code, StringComparison.OrdinalIgnoreCase));
}

// Turns fetched catalogs into what the user can do: update the agent, install
// or update plugins. Pure — the fetching and installing happen elsewhere.
public static class UpdateCheck
{
    // installed: plugin code -> installed version; lockEntries: the plugins
    // that came from a catalog (PluginLock).
    public static UpdateCheckResult Evaluate(
        Version agentVersion,
        Version apiVersion,
        IReadOnlyDictionary<string, Version?> installed,
        IReadOnlyDictionary<string, PluginLock.Entry> lockEntries,
        IReadOnlyList<SourceResult> sources,
        SourceResult? agentIndex = null)
    {
        // The agent only ever comes from its own index, never from a plugin
        // catalog — not even the official one.
        var agentUpdate = agentIndex?.Index?.Agent is { } agent
            && TryParse(agent.Version, out var latest) && latest > Normalize(agentVersion)
                ? agent
                : null;

        var offers = new Dictionary<string, PluginOffer>(StringComparer.OrdinalIgnoreCase);

        // Official first, so it wins a code two catalogs both claim.
        foreach (var result in sources.OrderByDescending(r => r.Source.IsOfficial))
        {
            if (result.Index == null)
                continue;

            foreach (var plugin in result.Index.Plugins)
            {
                if (offers.ContainsKey(plugin.Code) || !TryParse(plugin.Version, out var version))
                    continue;

                installed.TryGetValue(plugin.Code, out var installedVersion);
                var isInstalled = installed.ContainsKey(plugin.Code);

                // An installed plugin updates only from where it came from: a
                // catalog-installed one from its own catalog, a bundled or
                // official one (also one installed from the agent's index
                // before the plugins moved) from the official catalog. Another
                // catalog cannot take it over by listing the same code.
                if (isInstalled)
                {
                    var origin = lockEntries.TryGetValue(plugin.Code, out var entry) ? entry.Source : null;
                    var fromHere = origin != null && !CatalogClient.IsOfficialOrigin(origin)
                        ? string.Equals(origin, result.Source.Url, StringComparison.OrdinalIgnoreCase)
                        : result.Source.IsOfficial;

                    if (!fromHere)
                        continue;
                }

                var state = !IsApiCompatible(plugin.ApiVersion, apiVersion)
                    ? PluginOfferState.NeedsNewerAgent
                    : !isInstalled
                        ? PluginOfferState.Available
                        : installedVersion == null || version > Normalize(installedVersion)
                            ? PluginOfferState.UpdateAvailable
                            : PluginOfferState.UpToDate;

                offers[plugin.Code] = new PluginOffer(plugin, result.Source, installedVersion?.ToString(3), state);
            }
        }

        return new UpdateCheckResult(agentUpdate, offers.Values.OrderBy(o => o.Plugin.Name).ToArray(), sources,
            DateTimeOffset.Now, agentIndex);
    }

    // The rule PluginManager applies to a loaded plugin, for a catalog entry:
    // same major, and a minor the agent has caught up with.
    public static bool IsApiCompatible(string required, Version provided)
    {
        return Version.TryParse(required, out var version)
            && version.Major == provided.Major
            && version.Minor <= provided.Minor;
    }

    // "1.2.0" only: prerelease versions wait for a beta channel.
    private static bool TryParse(string value, out Version version)
    {
        if (Version.TryParse(value, out var parsed))
        {
            version = Normalize(parsed);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    // 1.2 and 1.2.0.0 compare equal to 1.2.0.
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));
}

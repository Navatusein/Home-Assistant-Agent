using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace HomeAssistant.Agent.Core.Updates;

// Fetches catalogs and the files they point to.
public sealed class CatalogClient
{
    public const string OfficialRepository = "https://github.com/Navatusein/Home-Assistant-Agent";

    public const string OfficialPluginsRepository = "https://github.com/Navatusein/Home-Assistant-Agent-Plugins";

    // The official plugin catalog — the only one: the plugins live in their
    // own repository.
    public static string OfficialUrl { get; } = ToIndexUrl(OfficialPluginsRepository);

    public static CatalogSource OfficialSource { get; } = new(OfficialUrl, IsOfficial: true);

    // Where the agent's releases are listed (its "agent" block). Read for the
    // agent's own update only: it is not a plugin catalog, and the plugins it
    // listed before they moved are not offered from it any more.
    public static string AgentIndexUrl { get; } = ToIndexUrl(OfficialRepository);

    public static bool IsOfficialUrl(string url) => string.Equals(url, OfficialUrl, StringComparison.OrdinalIgnoreCase);

    // A plugin installed from the agent's index before the official plugins
    // moved: it is official all the same and updates from the official catalog.
    public static bool IsOfficialOrigin(string url) =>
        IsOfficialUrl(url) || string.Equals(url, AgentIndexUrl, StringComparison.OrdinalIgnoreCase);

    // The plugin catalogs an update check reads: the official one, then the
    // ones the user added (the official URLs among them count once, and the
    // agent's index is not a plugin catalog).
    public static IReadOnlyList<CatalogSource> GetSources(IEnumerable<string> userSources)
    {
        return new[] { OfficialSource }
            .Concat(userSources
                .Where(url => !IsOfficialOrigin(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(url => new CatalogSource(url, IsOfficial: false)))
            .ToArray();
    }

    private static readonly TimeSpan CatalogTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http;

    public CatalogClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();

        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("HomeAssistantAgent", AgentInfo.Version.ToString(3)));
    }

    // A source as the user types it: the index.json URL itself, or just the
    // repository ("https://github.com/owner/repo") whose catalog lives at the
    // standard place. null when it is neither.
    public static string? NormalizeSource(string input)
    {
        var text = input.Trim().TrimEnd('/');

        if (text.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            text = text[..^4];

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;

        if (uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return uri.AbsoluteUri;

        var segments = uri.AbsolutePath.Trim('/').Split('/');

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && segments.Length == 2
            ? ToIndexUrl($"https://github.com/{segments[0]}/{segments[1]}")
            : null;
    }

    public static string ToIndexUrl(string repository) => $"{repository.TrimEnd('/')}/releases/download/index/index.json";

    public async Task<SourceResult> FetchAsync(CatalogSource source, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CatalogTimeout);

            var json = await _http.GetStringAsync(source.Url, timeout.Token);
            return new SourceResult(source, CatalogIndex.Parse(json), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new SourceResult(source, null, ex is TaskCanceledException ? "timed out" : ex.Message);
        }
    }

    // Downloads to targetPath and checks the file against the catalog's hash;
    // a mismatch deletes the file and throws — nothing unverified is kept.
    public async Task DownloadAsync(string url, string sha256, string targetPath,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(targetPath);

            var buffer = new byte[81920];
            long read = 0;
            int count;

            while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                read += count;

                if (total > 0)
                    progress?.Report((double) read / total.Value);
            }
        }

        if (!HashMatches(targetPath, sha256))
        {
            File.Delete(targetPath);
            throw new InvalidDataException("The download does not match the catalog's checksum.");
        }
    }

    public static bool HashMatches(string path, string sha256)
    {
        using var stream = File.OpenRead(path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(stream));

        return string.Equals(actual, sha256.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

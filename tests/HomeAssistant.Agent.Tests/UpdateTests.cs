using System.Security.Cryptography;
using HomeAssistant.Agent.Core.Updates;

namespace HomeAssistant.Agent.Tests;

public class UpdateCheckTests
{
    private static readonly Version Api = new(2, 0);
    private static readonly CatalogSource Official = new(CatalogClient.OfficialUrl, IsOfficial: true);
    private static readonly CatalogSource AgentIndex = new(CatalogClient.AgentIndexUrl, IsOfficial: true);
    private static readonly CatalogSource Other = new("https://github.com/someone/plugins/releases/download/index/index.json", false);

    private static CatalogPlugin Plugin(string code, string version, string api = "2.0") => new()
    {
        Code = code,
        Name = code,
        Version = version,
        ApiVersion = api,
        Url = $"https://example.test/{code}.dll",
        Sha256 = "00",
    };

    private static SourceResult Source(CatalogSource source, CatalogAgent? agent = null, params CatalogPlugin[] plugins) =>
        new(source, new CatalogIndex { Agent = agent, Plugins = plugins }, null);

    private static UpdateCheckResult Evaluate(
        Dictionary<string, Version?>? installed = null,
        Dictionary<string, PluginLock.Entry>? lockEntries = null,
        string agentVersion = "1.0.0",
        SourceResult? agentIndex = null,
        params SourceResult[] sources) =>
        UpdateCheck.Evaluate(Version.Parse(agentVersion), Api,
            installed ?? new Dictionary<string, Version?>(StringComparer.OrdinalIgnoreCase),
            lockEntries ?? new Dictionary<string, PluginLock.Entry>(StringComparer.OrdinalIgnoreCase),
            sources, agentIndex);

    // Not from a plugin catalog, even the official one.
    [Fact]
    public void Offers_a_newer_agent_from_the_agent_index_only()
    {
        var release = new CatalogAgent { Version = "1.1.0", Url = "https://x/setup.exe", Sha256 = "00" };

        Assert.Equal("1.1.0", Evaluate(agentIndex: Source(AgentIndex, release)).AgentUpdate?.Version);
        Assert.Null(Evaluate(agentVersion: "1.1.0", agentIndex: Source(AgentIndex, release)).AgentUpdate);
        Assert.Null(Evaluate(sources: Source(Official, release)).AgentUpdate);
        Assert.Null(Evaluate(sources: Source(Other, release)).AgentUpdate);
    }

    [Fact]
    public void Prerelease_versions_are_ignored()
    {
        var release = new CatalogAgent { Version = "2.0.0-beta.1", Url = "https://x/setup.exe", Sha256 = "00" };

        Assert.Null(Evaluate(agentIndex: Source(AgentIndex, release)).AgentUpdate);
    }

    [Fact]
    public void Classifies_plugins()
    {
        var installed = new Dictionary<string, Version?>(StringComparer.OrdinalIgnoreCase)
        {
            ["old"] = new Version(1, 0, 0),
            ["current"] = new Version(1, 2, 0),
        };

        var result = Evaluate(installed, sources: Source(Official, null,
            Plugin("old", "1.1.0"), Plugin("current", "1.2.0"), Plugin("new", "1.0.0"), Plugin("future", "1.0.0", api: "2.1")));

        Assert.Equal(PluginOfferState.UpdateAvailable, result.FindPlugin("old")!.State);
        Assert.Equal(PluginOfferState.UpToDate, result.FindPlugin("current")!.State);
        Assert.Equal(PluginOfferState.Available, result.FindPlugin("new")!.State);
        Assert.Equal(PluginOfferState.NeedsNewerAgent, result.FindPlugin("future")!.State);
    }

    // Another catalog listing the same code must not be able to replace a
    // bundled plugin, nor one installed from somewhere else.
    [Fact]
    public void An_installed_plugin_only_updates_from_where_it_came_from()
    {
        var installed = new Dictionary<string, Version?>(StringComparer.OrdinalIgnoreCase)
        {
            ["bundled"] = new Version(1, 0, 0),
            ["theirs"] = new Version(1, 0, 0),
        };

        var lockEntries = new Dictionary<string, PluginLock.Entry>(StringComparer.OrdinalIgnoreCase)
        {
            ["theirs"] = new("1.0.0", Other.Url, "00", "theirs.dll"),
        };

        var result = Evaluate(installed, lockEntries, sources:
        [
            Source(Official, null, Plugin("theirs", "9.0.0")),
            Source(Other, null, Plugin("bundled", "9.0.0"), Plugin("theirs", "1.1.0")),
        ]);

        Assert.Null(result.FindPlugin("bundled"));
        Assert.Equal("1.1.0", result.FindPlugin("theirs")!.Plugin.Version);
        Assert.Equal(Other, result.FindPlugin("theirs")!.Source);
    }

    [Fact]
    public void The_official_catalog_wins_a_new_plugin_both_list()
    {
        var result = Evaluate(sources: [Source(Other, null, Plugin("x", "5.0.0")), Source(Official, null, Plugin("x", "1.0.0"))]);

        Assert.Equal(Official, result.FindPlugin("x")!.Source);
    }

    [Fact]
    public void Sources_are_the_official_catalog_then_the_users()
    {
        var sources = CatalogClient.GetSources(
            [Other.Url, CatalogClient.OfficialUrl.ToUpperInvariant(), CatalogClient.AgentIndexUrl, Other.Url]);

        Assert.Equal([Official, Other], sources);
    }

    // A bundled plugin, or one installed from the agent's index before the
    // official plugins moved, updates from the official catalog; what the
    // agent's index still lists is not offered.
    [Fact]
    public void Official_plugins_update_from_the_official_catalog()
    {
        var installed = new Dictionary<string, Version?>(StringComparer.OrdinalIgnoreCase)
        {
            ["bundled"] = new Version(1, 0, 0),
            ["moved"] = new Version(1, 0, 0),
        };

        var lockEntries = new Dictionary<string, PluginLock.Entry>(StringComparer.OrdinalIgnoreCase)
        {
            ["moved"] = new("1.0.0", CatalogClient.AgentIndexUrl, "00", "moved.dll"),
        };

        var result = Evaluate(installed, lockEntries,
            agentIndex: Source(AgentIndex, null, Plugin("bundled", "9.0.0"), Plugin("stale", "1.0.0")),
            sources: Source(Official, null, Plugin("bundled", "1.2.0"), Plugin("moved", "1.2.0")));

        Assert.Equal("1.2.0", result.FindPlugin("bundled")!.Plugin.Version);
        Assert.Equal(Official, result.FindPlugin("bundled")!.Source);
        Assert.Equal(PluginOfferState.UpdateAvailable, result.FindPlugin("moved")!.State);
        Assert.Null(result.FindPlugin("stale"));
    }

    // The Windows plugin is not offered on a Mac, nor any plugin for an OS
    // this is not.
    [Fact]
    public void Plugins_for_another_system_are_not_offered()
    {
        var foreign = Plugin("foreign", "1.0.0") with { Platforms = ["nosuchos"] };
        var anywhere = Plugin("anywhere", "1.0.0") with { Platforms = [] };

        var result = Evaluate(sources: Source(Official, null, foreign, anywhere, Plugin("plain", "1.0.0")));

        Assert.Null(result.FindPlugin("foreign"));
        Assert.NotNull(result.FindPlugin("anywhere"));
        Assert.NotNull(result.FindPlugin("plain"));
    }

    [Fact]
    public void Platforms_are_read_from_the_catalog()
    {
        var index = CatalogIndex.Parse("""{"plugins":[{"code":"w","name":"W","version":"1.0.0","apiVersion":"2.0","url":"https://x/w.dll","sha256":"00","platforms":["windows"]}]}""");

        Assert.Equal(["windows"], index.Plugins[0].Platforms);
    }

    [Theory]
    [InlineData("2.0", true)]
    [InlineData("2.0.5", true)]
    [InlineData("1.9", false)]
    [InlineData("2.1", false)]
    [InlineData("garbage", false)]
    public void Api_compatibility(string required, bool compatible)
    {
        Assert.Equal(compatible, UpdateCheck.IsApiCompatible(required, Api));
    }
}

public class CatalogTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo", "https://github.com/owner/repo/releases/download/index/index.json")]
    [InlineData("https://github.com/owner/repo.git", "https://github.com/owner/repo/releases/download/index/index.json")]
    [InlineData(" https://github.com/owner/repo/ ", "https://github.com/owner/repo/releases/download/index/index.json")]
    [InlineData("https://example.com/my/index.json", "https://example.com/my/index.json")]
    [InlineData("http://github.com/owner/repo", null)]
    [InlineData("https://github.com/owner", null)]
    [InlineData("not a url", null)]
    public void Normalizes_sources(string input, string? expected)
    {
        Assert.Equal(expected, CatalogClient.NormalizeSource(input));
    }

    // The shape build/Update-Index.ps1 and tools/PluginManifest write.
    [Fact]
    public void Parses_an_index()
    {
        var index = CatalogIndex.Parse("""
            {
              "schema": 1,
              "updated": "2026-10-16T10:00:00Z",
              "agent": { "version": "1.1.0", "url": "https://x/setup.exe", "sha256": "ab" },
              "plugins": [
                { "code": "obs-plugin", "name": "OBS", "version": "1.0.0", "apiVersion": "2.0",
                  "url": "https://github.com/o/r/releases/download/plugin%2Fobs%2Fv1.0.0/HomeAssistant.Agent.Plugin.Obs.dll",
                  "sha256": "cd" }
              ]
            }
            """);

        Assert.Equal("1.1.0", index.Agent!.Version);
        Assert.Equal("HomeAssistant.Agent.Plugin.Obs.dll", Assert.Single(index.Plugins).FileName);
    }
}

public class PluginOperationsTests
{
    private static (string Path, string Sha256) WriteFile(TempDirectory directory, string name, string content)
    {
        var path = directory.File(name);
        File.WriteAllText(path, content);
        return (path, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
    }

    [Fact]
    public void Arguments_round_trip()
    {
        var request = new PluginRequest(
            [new PluginInstall(@"C:\Temp\a b\x.dll", "ab", "x-plugin", "1.0.0", "https://s/index.json", "x.dll")],
            [new PluginRemoval("y-plugin", "y.dll")],
            1234,
            "app");

        var parsed = PluginOperations.Parse(PluginOperations.ToArguments(request));

        Assert.Equal(request.Installs, parsed.Installs);
        Assert.Equal(request.Removals, parsed.Removals);
        Assert.Equal(1234, parsed.WaitForProcess);
        Assert.Equal("app", parsed.Relaunch);
    }

    [Fact]
    public void Installs_records_and_removes()
    {
        using var downloads = new TempDirectory();
        using var plugins = new TempDirectory();
        var pluginLock = new PluginLock(plugins.File("lock.json"));

        var (file, sha) = WriteFile(downloads, "x.dll", "new");
        File.WriteAllText(plugins.File("y.dll"), "old");

        PluginOperations.Apply(new PluginRequest(
            [new PluginInstall(file, sha, "x-plugin", "1.0.0", "src", "x.dll")],
            [new PluginRemoval("y-plugin", "y.dll")], null, null), plugins.Path, pluginLock);

        Assert.Equal("new", File.ReadAllText(plugins.File("x.dll")));
        Assert.False(File.Exists(plugins.File("y.dll")));
        Assert.False(File.Exists(plugins.File("x.dll.new")));
        Assert.Equal("src", pluginLock.Read()["x-plugin"].Source);
    }

    // The file is checked again by the elevated step: one swapped after the
    // download was verified is refused.
    [Fact]
    public void Refuses_a_file_that_does_not_match_its_hash()
    {
        using var downloads = new TempDirectory();
        using var plugins = new TempDirectory();
        var (file, sha) = WriteFile(downloads, "x.dll", "verified");

        File.WriteAllText(file, "swapped");

        Assert.Throws<InvalidDataException>(() => PluginOperations.Apply(new PluginRequest(
            [new PluginInstall(file, sha, "x", "1.0.0", "src", "x.dll")], [], null, null),
            plugins.Path, new PluginLock(plugins.File("lock.json"))));

        Assert.Empty(Directory.GetFiles(plugins.Path, "*.dll*"));
    }

    [Theory]
    [InlineData(@"..\evil.dll")]
    [InlineData(@"C:\Windows\evil.dll")]
    [InlineData("evil.exe")]
    [InlineData(".hidden.dll")]
    public void Refuses_file_names_that_leave_the_plugins_folder(string fileName)
    {
        using var plugins = new TempDirectory();

        Assert.Throws<ArgumentException>(() => PluginOperations.Apply(new PluginRequest(
            [], [new PluginRemoval("x", fileName)], null, null), plugins.Path, new PluginLock(plugins.File("lock.json"))));
    }
}

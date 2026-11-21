using System.Text;
using HomeAssistant.Agent.Core.Plugins;

namespace HomeAssistant.Agent.Tests;

public class EmbeddedNativeLibrariesTests
{
    private static readonly Dictionary<string, byte[]> Resources = new()
    {
        ["EmbeddedAssemblies/Noise.dll"] = Encoding.UTF8.GetBytes("managed"),
        ["EmbeddedNative/win-x64/libsodium.dll"] = Encoding.UTF8.GetBytes("sodium for windows"),
        ["EmbeddedNative/win-x64/helper.dll"] = Encoding.UTF8.GetBytes("helper"),
        ["EmbeddedNative/osx-arm64/libsodium.dylib"] = Encoding.UTF8.GetBytes("sodium for mac"),
    };

    private static EmbeddedNativeLibraries Create(string cacheRoot, params string[] runtimes) =>
        new(Resources.Keys,
            name => Resources.TryGetValue(name, out var content) ? new MemoryStream(content) : null,
            "sendspin-plugin", runtimes.Length > 0 ? runtimes : ["win-x64", "win"], cacheRoot);

    [Theory]
    [InlineData("libsodium")]
    [InlineData("libsodium.dll")]
    [InlineData("sodium")]
    public void Resolves_the_names_a_DllImport_uses(string name)
    {
        using var temp = new TempDirectory();

        var path = Create(temp.Path).Resolve(name);

        Assert.NotNull(path);
        Assert.Equal("libsodium.dll", Path.GetFileName(path));
        Assert.Equal("sodium for windows", File.ReadAllText(path));
    }

    [Fact]
    public void Unpacks_every_library_of_the_runtime_into_one_folder()
    {
        using var temp = new TempDirectory();

        var path = Create(temp.Path).Resolve("libsodium")!;
        var directory = Path.GetDirectoryName(path)!;

        Assert.Equal(new[] { "helper.dll", "libsodium.dll" },
            Directory.GetFiles(directory).Select(Path.GetFileName).Order().ToArray());
        Assert.StartsWith(Path.Combine(temp.Path, "sendspin-plugin"), directory);
    }

    [Fact]
    public void Takes_only_the_running_runtime()
    {
        using var temp = new TempDirectory();

        var mac = Create(temp.Path, "osx-arm64", "osx", "unix");

        Assert.Equal("sodium for mac", File.ReadAllText(mac.Resolve("libsodium")!));
        Assert.Null(mac.Resolve("helper"));
        Assert.Null(Create(temp.Path, "linux-x64", "linux", "unix").Resolve("libsodium"));
    }

    [Fact]
    public void Does_not_resolve_managed_assemblies_or_unknown_libraries()
    {
        using var temp = new TempDirectory();

        var libraries = Create(temp.Path);

        Assert.Null(libraries.Resolve("Noise"));
        Assert.Null(libraries.Resolve("libc"));
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "sendspin-plugin")));
    }

    // A file changed in the cache is not trusted: the plugin gets its own copy back.
    [Fact]
    public void Restores_a_changed_file()
    {
        using var temp = new TempDirectory();

        var path = Create(temp.Path).Resolve("libsodium")!;
        File.WriteAllText(path, "tampered");

        Assert.Equal(path, Create(temp.Path).Resolve("libsodium"));
        Assert.Equal("sodium for windows", File.ReadAllText(path));
    }
}

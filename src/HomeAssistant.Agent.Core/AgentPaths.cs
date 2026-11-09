using HomeAssistant.Agent.Plugin.Base;

namespace HomeAssistant.Agent.Core;

// Where the agent keeps things. Two places:
//
//   install directory  the binaries and the bundled plugins — Program Files
//                      once installed, read-only for users and replaced
//                      wholesale by an update;
//   data directory     settings, plugin state, discovery records, logs —
//                      %ProgramData%\HomeAssistantAgent on Windows, shared
//                      by the desktop app and the background service.
//
// A portable install (a development build, an unzipped copy) keeps its data
// next to the binaries instead. It is recognised by a "portable" marker file
// in the install directory — the build puts one into bin — or by a
// Settings.json already sitting there, which is how every build before this
// one stored it.
public static class AgentPaths
{
    public const string PortableMarker = "portable";
    public const string DataFolderName = "HomeAssistantAgent";

    private const string SettingsFileName = "Settings.json";

    public static string InstallDirectory => AppContext.BaseDirectory;

    public static bool IsPortable { get; } =
        File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker))
        || File.Exists(Path.Combine(AppContext.BaseDirectory, SettingsFileName));

    public static string DataDirectory { get; } = IsPortable
        ? AppContext.BaseDirectory
        : Path.Combine(SharedDataRoot(), DataFolderName);

    // Shared by the desktop app and the background service on every system:
    // %ProgramData% on Windows, /Library/Application Support on macOS (the
    // installer grants the users write access, as it does on Windows). Other
    // systems keep it per user until they have a service of their own.
    private static string SharedDataRoot()
    {
        if (OperatingSystem.IsWindows())
            return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        if (OperatingSystem.IsMacOS())
            return "/Library/Application Support";

        return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    public static string SettingsFile => Path.Combine(DataDirectory, SettingsFileName);

    public static string LogsDirectory => Path.Combine(DataDirectory, "Logs");

    // Each host keeps its own plugin state: both rewrite their file whole, and
    // they run as different users.
    public static string GetStorageFile(PluginHostMode host) =>
        Path.Combine(DataDirectory, host == PluginHostMode.Service ? "PersistentStorage.Service.json" : "PersistentStorage.json");

    // The plugins shipped with the agent, next to the binaries.
    public static string BundledPluginsDirectory => Path.Combine(InstallDirectory, "Plugins");

    // Where plugins from a catalog go. Only an elevated step may write there:
    // the service (SYSTEM, root) loads every dll it finds.
    //
    // On Windows that is the bundled folder itself (Program Files is admin-only
    // already). On macOS the app bundle must stay as it was signed and is
    // replaced whole by an update, so they live with the data, in a folder the
    // setup step gives to root. A portable copy keeps both next to itself.
    public static string InstalledPluginsDirectory => OperatingSystem.IsMacOS() && !IsPortable
        ? Path.Combine(DataDirectory, "Plugins")
        : BundledPluginsDirectory;

    // Every folder plugins load from, the installed ones first: a catalog
    // update of a bundled plugin wins over the copy in the bundle.
    public static IReadOnlyList<string> PluginDirectories =>
        new[] { InstalledPluginsDirectory, BundledPluginsDirectory }
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();

    // Created on first use; an installer creates it up front with the access
    // rights the desktop app and the service both need.
    public static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}

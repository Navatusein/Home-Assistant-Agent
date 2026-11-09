namespace HomeAssistant.Agent.Core.Services;

// Brings the data of an earlier copy of the agent — a development build or an
// unzipped one, which kept everything next to its binaries — into this
// installation's data directory.
public static class DataImport
{
    private const string SettingsFile = "Settings.json";

    // The desktop app's state only. The service's plugin state is not taken:
    // the service has no tokens to keep, and it holds its file open anyway.
    private static readonly string[] Patterns = [SettingsFile, "PersistentStorage.json", "Discovery.*.json"];

    // Whether the folder looks like an agent's data at all.
    public static bool ContainsData(string directory) => File.Exists(Path.Combine(directory, SettingsFile));

    // Copies over whatever is there, replacing files of the same name. Returns
    // the names copied. The running agent holds the settings and plugin state
    // in memory — restart it afterwards.
    public static IReadOnlyList<string> Import(string sourceDirectory, string targetDirectory)
    {
        if (Path.GetFullPath(sourceDirectory).TrimEnd('\\')
            .Equals(Path.GetFullPath(targetDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("That is this installation's own data folder.");
        }

        if (!ContainsData(sourceDirectory))
            throw new InvalidOperationException($"No {SettingsFile} in {sourceDirectory}.");

        Directory.CreateDirectory(targetDirectory);

        var copied = new List<string>();

        foreach (var pattern in Patterns)
        {
            foreach (var file in Directory.GetFiles(sourceDirectory, pattern))
            {
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(targetDirectory, name), overwrite: true);
                copied.Add(name);
            }
        }

        return copied;
    }
}

using System.Text.Json;

namespace HomeAssistant.Agent.Core.Updates;

// Installs and removes plugin files — the part of a plugin change that needs
// administrator rights, since the Plugins folder is admin-only (the service
// loads every dll in it as SYSTEM). The desktop app downloads and verifies the
// files, then hands this list to the elevated step
// (HomeAssistant.Agent.Service.exe plugins ...), which applies it.
//
// The elevated step checks every file against its hash again, on its own copy:
// the desktop app's download sits in a user-writable folder, and a file
// swapped after the app's check must not get installed.
public sealed record PluginInstall(string SourceFile, string Sha256, string Code, string Version, string Source, string FileName);

public sealed record PluginRemoval(string Code, string FileName);

public sealed record PluginRequest(
    IReadOnlyList<PluginInstall> Installs,
    IReadOnlyList<PluginRemoval> Removals,
    int? WaitForProcess,
    string? Relaunch);

// What the elevated step did, for the desktop app to show after it restarts.
public sealed record PluginReport(bool Success, string Message, DateTimeOffset At);

public static class PluginOperations
{
    public const string Verb = "plugins";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ReportPath => Path.Combine(AgentPaths.DataDirectory, "plugins.report.json");

    // Command line of the elevated step. Arguments, not a file: what the user
    // consented to in the UAC prompt cannot change afterwards.
    public static IReadOnlyList<string> ToArguments(PluginRequest request)
    {
        var args = new List<string> { Verb };

        if (request.WaitForProcess is { } pid)
            args.AddRange(["--wait", pid.ToString()]);

        if (request.Relaunch is { } relaunch)
            args.AddRange(["--relaunch", relaunch]);

        foreach (var install in request.Installs)
            args.AddRange(["--install", install.SourceFile, install.Sha256, install.Code, install.Version, install.Source, install.FileName]);

        foreach (var removal in request.Removals)
            args.AddRange(["--remove", removal.Code, removal.FileName]);

        return args;
    }

    public static PluginRequest Parse(IReadOnlyList<string> args)
    {
        var installs = new List<PluginInstall>();
        var removals = new List<PluginRemoval>();
        int? wait = null;
        string? relaunch = null;

        var i = args.Count > 0 && args[0] == Verb ? 1 : 0;

        string Next() => i < args.Count ? args[i++] : throw new ArgumentException("Incomplete plugin command line.");

        while (i < args.Count)
        {
            switch (Next())
            {
                case "--wait":
                    wait = int.Parse(Next());
                    break;
                case "--relaunch":
                    relaunch = Next();
                    break;
                case "--install":
                    installs.Add(new PluginInstall(Next(), Next(), Next(), Next(), Next(), Next()));
                    break;
                case "--remove":
                    removals.Add(new PluginRemoval(Next(), Next()));
                    break;
                case var other:
                    throw new ArgumentException($"Unknown plugin argument '{other}'.");
            }
        }

        return new PluginRequest(installs, removals, wait, relaunch);
    }

    // Applies everything or throws on the first problem. Each install is
    // staged as <name>.new next to its target, verified there, then swapped
    // in; the lock file records where it came from.
    public static void Apply(PluginRequest request, string pluginsDirectory, PluginLock pluginLock)
    {
        Directory.CreateDirectory(pluginsDirectory);

        var entries = pluginLock.Read();

        foreach (var install in request.Installs)
        {
            var target = ResolveTarget(pluginsDirectory, install.FileName);
            var staged = target + ".new";

            File.Copy(install.SourceFile, staged, overwrite: true);

            if (!CatalogClient.HashMatches(staged, install.Sha256))
            {
                File.Delete(staged);
                throw new InvalidDataException($"{install.FileName} changed after it was downloaded — not installed.");
            }

            File.Move(staged, target, overwrite: true);

            // A plugin renamed between versions leaves its old file behind.
            if (entries.TryGetValue(install.Code, out var previous)
                && !string.Equals(previous.FileName, install.FileName, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(ResolveTarget(pluginsDirectory, previous.FileName));
            }

            entries[install.Code] = new PluginLock.Entry(install.Version, install.Source, install.Sha256, install.FileName);
        }

        foreach (var removal in request.Removals)
        {
            File.Delete(ResolveTarget(pluginsDirectory, removal.FileName));
            entries.Remove(removal.Code);
        }

        pluginLock.Write(entries);
    }

    public static void WriteReport(PluginReport report)
    {
        Directory.CreateDirectory(AgentPaths.DataDirectory);
        File.WriteAllText(ReportPath, JsonSerializer.Serialize(report, JsonOptions));
    }

    // Read once: the report is about the last change, shown after the restart
    // that change caused.
    public static PluginReport? TakeReport()
    {
        if (!File.Exists(ReportPath))
            return null;

        try
        {
            return JsonSerializer.Deserialize<PluginReport>(File.ReadAllText(ReportPath));
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            try { File.Delete(ReportPath); } catch (IOException) { }
        }
    }

    // A bare "<name>.dll" inside the Plugins folder — never a path that leads
    // out of it. Separators of either system are refused outright: on macOS a
    // backslash is an ordinary character, and a name that is a path on
    // Windows must not pass as a strange file name there.
    private static string ResolveTarget(string pluginsDirectory, string fileName)
    {
        if (fileName != Path.GetFileName(fileName)
            || fileName.IndexOfAny(['\\', '/', ':']) >= 0
            || !fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith('.'))
        {
            throw new ArgumentException($"'{fileName}' is not a plugin file name.");
        }

        return Path.Combine(pluginsDirectory, fileName);
    }
}

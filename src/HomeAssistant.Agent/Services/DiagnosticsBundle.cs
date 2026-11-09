using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeAssistant.Agent.Core;
using HomeAssistant.Agent.Core.Logging;
using HomeAssistant.Agent.Core.Plugins;
using HomeAssistant.Agent.Core.Services;
using HomeAssistant.Agent.Core.Settings;

namespace HomeAssistant.Agent.Services;

// Everything a bug report needs, in one zip the user can attach:
//   info.txt        versions, OS, MQTT state, plugins and load errors;
//   settings.json   Settings.json with secrets masked;
//   discovery/      what each host last published to Home Assistant;
//   logs/           the recent log files of the app and the service.
//
// Left out on purpose: PersistentStorage*.json — it holds the Discord and
// Teams tokens and nothing that helps diagnose a problem.
public static class DiagnosticsBundle
{
    private static readonly TimeSpan LogAge = TimeSpan.FromDays(3);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string DefaultFileName => $"HomeAssistantAgent-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

    public static void Create(string zipPath, PluginManager pluginManager, AgentService agentService)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        AddText(zip, "info.txt", BuildInfo(pluginManager, agentService));
        AddText(zip, "settings.json", SettingsStore.Serialize(MaskSettings(agentService.Settings)));

        foreach (var ledger in Directory.GetFiles(AgentPaths.DataDirectory, "Discovery.*.json"))
            AddFile(zip, "discovery/" + Path.GetFileName(ledger), ledger);

        if (Directory.Exists(AgentLogging.LogsDirectory))
        {
            var since = DateTime.Now - LogAge;

            foreach (var log in new DirectoryInfo(AgentLogging.LogsDirectory).GetFiles("*.log"))
            {
                if (log.LastWriteTime >= since)
                    AddFile(zip, "logs/" + log.Name, log.FullName);
            }
        }
    }

    private static string BuildInfo(PluginManager pluginManager, AgentService agentService)
    {
        var info = new StringBuilder();

        info.AppendLine($"Created:      {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        info.AppendLine($"Agent:        {typeof(AgentService).Assembly.GetName().Version?.ToString(3) ?? "unknown"}");
        info.AppendLine($"OS:           {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        info.AppendLine($"Runtime:      {RuntimeInformation.FrameworkDescription}");
        info.AppendLine($"Installed in: {AgentPaths.InstallDirectory}");
        info.AppendLine($"Data in:      {AgentPaths.DataDirectory}{(AgentPaths.IsPortable ? " (portable)" : string.Empty)}");
        info.AppendLine($"MQTT:         {(agentService.IsConnected ? "connected" : "not connected")}");
        info.AppendLine($"Log level:    {LogLevels.ToName(AgentLogging.Levels.DefaultLevel)}");
        info.AppendLine();
        info.AppendLine("Plugins:");

        foreach (var plugin in pluginManager.Plugins)
        {
            var assembly = plugin.GetType().Assembly;

            info.AppendLine($"  {plugin.PluginCode,-20} {assembly.GetName().Version?.ToString(3),-10} "
                + $"{(plugin.IsEnabled ? "enabled" : "disabled"),-9} {Path.GetFileName(assembly.Location)}");
        }

        if (pluginManager.Errors.Count > 0)
        {
            info.AppendLine();
            info.AppendLine("Load errors:");

            foreach (var error in pluginManager.Errors)
                info.AppendLine($"  {error.Source}: {error.Message}");
        }

        return info.ToString();
    }

    // Values under a secret-looking key are replaced outright; everything else
    // goes through the log masker, which also catches secrets nested in the
    // JSON a list setting is stored as.
    private static AgentSettings MaskSettings(AgentSettings settings)
    {
        var masked = settings.Clone();

        foreach (var values in masked.Sections.Values)
            MaskValues(values);

        foreach (var plugin in masked.Plugins.Values)
            MaskObject(plugin.Config);

        return masked;
    }

    // Walks into lists too: a list entry has keys of its own ("Password").
    private static void MaskObject(JsonObject values)
    {
        foreach (var (key, node) in values.ToArray())
        {
            switch (node)
            {
                case JsonObject child:
                    MaskObject(child);
                    break;

                case JsonArray array:
                    foreach (var entry in array.OfType<JsonObject>())
                        MaskObject(entry);
                    break;

                case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                    var text = value.GetValue<string>();

                    values[key] = SecretMasker.IsSecretName(key) && !string.IsNullOrEmpty(text)
                        ? SecretMasker.Mask
                        : SecretMasker.MaskText(text);
                    break;
            }
        }
    }

    private static void MaskValues(Dictionary<string, string> values)
    {
        foreach (var (key, value) in values.ToArray())
        {
            values[key] = SecretMasker.IsSecretName(key) && !string.IsNullOrEmpty(value)
                ? SecretMasker.Mask
                : SecretMasker.MaskText(value);
        }
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
        writer.Write(text);
    }

    // Shared read: the current log files are still open for writing.
    private static void AddFile(ZipArchive zip, string name, string path)
    {
        try
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = zip.CreateEntry(name).Open();
            source.CopyTo(target);
        }
        catch (IOException ex)
        {
            AddText(zip, name + ".error.txt", $"Could not read {path}: {ex.Message}");
        }
    }
}

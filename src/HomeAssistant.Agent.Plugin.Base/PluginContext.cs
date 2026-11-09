using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Base;

// Everything a plugin gets from the host, handed over once through
// BasePlugin.Initialize. Replaces the process-wide statics plugins used to
// reach for (BasePlugin.LoggerFactory, PluginPersistentStorage), so a plugin's
// dependencies are explicit and per plugin.
public sealed class PluginContext
{
    // The desktop app or the Windows service; decides which entity set the
    // plugin exposes.
    public required PluginHostMode HostMode { get; init; }

    // Loggers created here are tagged with the plugin in the log
    // ("discord-plugin/DiscordClient") and follow its own log level.
    public required ILoggerFactory LoggerFactory { get; init; }

    // State that survives a restart (pairing and auth tokens), private to the
    // plugin: keys of different plugins never collide.
    public required IPluginStorage Storage { get; init; }

    // Version of the agent the plugin runs in, e.g. for a user agent string.
    public required string AgentVersion { get; init; }
}

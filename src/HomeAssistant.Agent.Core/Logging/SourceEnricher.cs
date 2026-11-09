using System.Collections.Concurrent;
using Serilog.Core;
using Serilog.Events;

namespace HomeAssistant.Agent.Core.Logging;

// Turns the logger category into something readable and filterable:
//   "LogSource" — the short type name, prefixed with the plugin code for plugin
//              types: "discord-plugin/DiscordClient", "AgentService";
//   "LogPlugin" — the plugin code alone, for per-plugin levels and filtering.
//
// Plugins are matched by their assembly name as a namespace prefix, which the
// plugin manager registers as it loads them. Every category is resolved once
// and cached: the set of categories is small and fixed.
public sealed class SourceEnricher : ILogEventEnricher
{
    public const string SourceProperty = "LogSource";
    public const string PluginProperty = "LogPlugin";

    private const string AgentSource = "Agent";

    private static readonly ConcurrentDictionary<string, string> PluginNamespaces = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, (string Source, string? Plugin)> _cache = new(StringComparer.Ordinal);

    public static void RegisterPlugin(string rootNamespace, string pluginCode)
    {
        PluginNamespaces[rootNamespace + "."] = pluginCode;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var category = logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var value)
            && value is ScalarValue { Value: string context }
                ? context
                : null;

        var (source, plugin) = category == null ? (AgentSource, null) : _cache.GetOrAdd(category, Resolve);

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(SourceProperty, source));

        if (plugin != null)
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(PluginProperty, plugin));
    }

    private static (string Source, string? Plugin) Resolve(string category)
    {
        var typeName = ShortTypeName(category);

        foreach (var (prefix, plugin) in PluginNamespaces)
        {
            if (category.StartsWith(prefix, StringComparison.Ordinal))
                return ($"{plugin}/{typeName}", plugin);
        }

        return (typeName, null);
    }

    // "HomeAssistant.Agent.Core.Services.AgentService" -> "AgentService";
    // generic arguments are dropped first, they may contain dots of their own.
    private static string ShortTypeName(string category)
    {
        var generic = category.IndexOf('<');
        var name = generic >= 0 ? category[..generic] : category;
        var lastDot = name.LastIndexOf('.');

        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }
}

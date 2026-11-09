using Serilog.Core;
using Serilog.Events;

namespace HomeAssistant.Agent.Core.Logging;

// Decides at runtime which events get written: one default level plus
// per-plugin overrides, changeable from the settings without a restart.
//
// Two layers on purpose. The level switch is Serilog's minimum level and is
// held at the lowest level anyone asked for, so with everything at Info a
// Trace call costs nothing — callers such as the Discord client serialize
// whole frames for Trace. The filter then applies the exact level per event.
public sealed class LogLevelController
{
    private volatile State _state = new(LogEventLevel.Information, new Dictionary<string, LogEventLevel>());

    public LoggingLevelSwitch MinimumLevel { get; } = new(LogEventLevel.Information);

    public LogEventLevel DefaultLevel => _state.Default;

    public void Apply(LogEventLevel defaultLevel, IReadOnlyDictionary<string, LogEventLevel> pluginLevels)
    {
        var overrides = new Dictionary<string, LogEventLevel>(pluginLevels, StringComparer.OrdinalIgnoreCase);

        _state = new State(defaultLevel, overrides);

        MinimumLevel.MinimumLevel = overrides.Count == 0
            ? defaultLevel
            : (LogEventLevel) Math.Min((int) defaultLevel, (int) overrides.Values.Min());
    }

    public bool IsEnabled(LogEvent logEvent)
    {
        var state = _state;

        if (state.Overrides.Count > 0
            && logEvent.Properties.TryGetValue(SourceEnricher.PluginProperty, out var value)
            && value is ScalarValue { Value: string plugin }
            && state.Overrides.TryGetValue(plugin, out var pluginLevel))
        {
            return logEvent.Level >= pluginLevel;
        }

        return logEvent.Level >= state.Default;
    }

    private sealed record State(LogEventLevel Default, IReadOnlyDictionary<string, LogEventLevel> Overrides);
}

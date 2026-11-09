using Serilog.Core;
using Serilog.Events;

namespace HomeAssistant.Agent.Core.Logging;

// Adds a "Lvl" property with a short, friendly level name (Info / Warn / Error)
// for the output template. Serilog's built-in formats only offer the full name
// (Information/Warning) or a 3-letter code (INF/WRN), neither of which is what
// we want to see in the log.
public sealed class LevelNameEnricher : ILogEventEnricher
{
    public const string PropertyName = "Lvl";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(PropertyName, LogLevels.ToName(logEvent.Level)));
    }
}

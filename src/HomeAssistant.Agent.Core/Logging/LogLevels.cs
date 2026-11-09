using Serilog.Events;

namespace HomeAssistant.Agent.Core.Logging;

// The level names the agent shows and stores: the same short names the log
// file uses ([Info], [Warn]), not Serilog's Information/Warning.
public static class LogLevels
{
    public const string Trace = "Trace";
    public const string Debug = "Debug";
    public const string Info = "Info";
    public const string Warn = "Warn";
    public const string Error = "Error";

    public static IReadOnlyList<string> Names { get; } = [Trace, Debug, Info, Warn, Error];

    public static string ToName(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => Trace,
        LogEventLevel.Debug => Debug,
        LogEventLevel.Information => Info,
        LogEventLevel.Warning => Warn,
        LogEventLevel.Error => Error,
        LogEventLevel.Fatal => "Fatal",
        _ => level.ToString()
    };

    // Accepts the short names and Serilog's own (case-insensitive), so a
    // hand-edited Settings.json with "Information" still works.
    public static bool TryParse(string? value, out LogEventLevel level)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "trace" or "verbose":
                level = LogEventLevel.Verbose;
                return true;
            case "debug":
                level = LogEventLevel.Debug;
                return true;
            case "info" or "information":
                level = LogEventLevel.Information;
                return true;
            case "warn" or "warning":
                level = LogEventLevel.Warning;
                return true;
            case "error":
                level = LogEventLevel.Error;
                return true;
            case "fatal":
                level = LogEventLevel.Fatal;
                return true;
            default:
                level = default;
                return false;
        }
    }
}

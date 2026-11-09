using Avalonia;
using Avalonia.Logging;
using AvaloniaLevel = Avalonia.Logging.LogEventLevel;

namespace HomeAssistant.Agent.Logging;

// Avalonia reports its own problems — a binding to a missing property, a
// failed layout — through a logger nobody reads. This forwards the warnings
// and errors into the agent's log, so a broken binding shows up the same way
// any other error does.
public sealed class AvaloniaLogSink : ILogSink
{
    private const string SourceName = "Avalonia";

    // Binding warnings are mostly a path meeting a null on its way (a template
    // built before its DataContext arrives) and resolve a moment later; a
    // misspelled path fails the build already, the bindings being compiled.
    public bool IsEnabled(AvaloniaLevel level, string area) =>
        level >= (area == LogArea.Binding ? AvaloniaLevel.Error : AvaloniaLevel.Warning);

    public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate) =>
        Log(level, area, source, messageTemplate, []);

    public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
            return;

        Serilog.Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, $"{SourceName}.{area}")
            .Write(ToSerilog(level), $"{messageTemplate} ({{AvaloniaSource}})", [.. propertyValues, source?.GetType().Name]);
    }

    private static Serilog.Events.LogEventLevel ToSerilog(AvaloniaLevel level) => level switch
    {
        AvaloniaLevel.Verbose => Serilog.Events.LogEventLevel.Verbose,
        AvaloniaLevel.Debug => Serilog.Events.LogEventLevel.Debug,
        AvaloniaLevel.Information => Serilog.Events.LogEventLevel.Information,
        AvaloniaLevel.Warning => Serilog.Events.LogEventLevel.Warning,
        AvaloniaLevel.Error => Serilog.Events.LogEventLevel.Error,
        _ => Serilog.Events.LogEventLevel.Fatal,
    };
}

public static class AvaloniaLogSinkExtensions
{
    public static AppBuilder LogToSerilog(this AppBuilder builder)
    {
        Logger.Sink = new AvaloniaLogSink();
        return builder;
    }
}

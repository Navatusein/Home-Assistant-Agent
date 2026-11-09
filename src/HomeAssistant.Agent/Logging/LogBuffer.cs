using System.Collections.ObjectModel;
using HomeAssistant.Agent.Core.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace HomeAssistant.Agent.Logging;

// One line on the Logs page — from the live buffer or parsed back from a file.
// Plugin is the plugin code for plugin sources and null for the agent itself.
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogEventLevel Level,
    string Host,
    string Source,
    string? Plugin,
    string Message,
    string? Exception)
{
    public string Time => Timestamp.ToString("HH:mm:ss.fff");

    public string LevelName => LogLevels.ToName(Level);

    // What the grid shows: the message plus the exception's first line, so a
    // failure is visible without opening the details.
    public string Summary => Exception == null ? Message : $"{Message} — {FirstLine(Exception)}";

    public bool HasException => Exception != null;

    // The first line every run writes (AgentLogging.LogStartup): where one
    // run of the app or the service ends and the next begins.
    public bool IsRunStart =>
        Message.StartsWith("Home Assistant Agent ", StringComparison.Ordinal)
        && Message.Contains(" starting as ", StringComparison.Ordinal);

    // For the level's colour on the Logs page.
    public bool IsMuted => Level <= LogEventLevel.Debug;

    public bool IsWarning => Level == LogEventLevel.Warning;

    public bool IsError => Level >= LogEventLevel.Error;

    // The entry in the log file's own format, for copying.
    public string FullText =>
        $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{LevelName}] [{Host}] {Source}: {Message}"
        + (Exception == null ? string.Empty : Environment.NewLine + Exception);

    private static string FirstLine(string text)
    {
        var newLine = text.IndexOfAny(['\r', '\n']);
        return newLine < 0 ? text : text[..newLine];
    }
}

// Serilog sink that keeps the most recent entries around so the Logs page can
// show them without reading back the file it just wrote.
//
// A singleton rather than a DI service: Serilog is configured before the host
// is built, so the sink has to exist before any container does.
public sealed class LogBuffer : ILogEventSink
{
    private const int Capacity = 2000;

    public static LogBuffer Instance { get; } = new();

    private readonly ObservableCollection<LogEntry> _entries = [];

    private LogBuffer()
    {
        Entries = new ReadOnlyObservableCollection<LogEntry>(_entries);
    }

    public ReadOnlyObservableCollection<LogEntry> Entries { get; }

    public void Emit(LogEvent logEvent)
    {
        var entry = new LogEntry(
            logEvent.Timestamp,
            logEvent.Level,
            GetString(logEvent, AgentLogging.HostProperty) ?? "app",
            GetString(logEvent, SourceEnricher.SourceProperty) ?? string.Empty,
            GetString(logEvent, SourceEnricher.PluginProperty),
            RenderMessage(logEvent),
            logEvent.Exception?.ToString());

        // Log events arrive on whatever thread produced them; an
        // ObservableCollection may only be touched by the UI thread. Events
        // logged before Avalonia starts come from that same (main) thread.
        var dispatcher = Avalonia.Threading.Dispatcher.UIThread;

        if (dispatcher.CheckAccess())
            Add(entry);
        else
            dispatcher.Post(() => Add(entry));
    }

    private void Add(LogEntry entry)
    {
        _entries.Add(entry);

        while (_entries.Count > Capacity)
            _entries.RemoveAt(0);
    }

    // Like the log file ({Message:lj}): strings without quotes, so the page
    // and the file read the same.
    private static string RenderMessage(LogEvent logEvent)
    {
        using var writer = new System.IO.StringWriter();
        MessageFormatter.Format(logEvent, writer);
        return writer.ToString();
    }

    private static readonly MessageTemplateTextFormatter MessageFormatter = new("{Message:lj}");

    private static string? GetString(LogEvent logEvent, string property) =>
        logEvent.Properties.TryGetValue(property, out var value) && value is ScalarValue { Value: string text }
            ? text
            : null;
}

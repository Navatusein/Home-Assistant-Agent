using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using HomeAssistant.Agent.Core.Logging;
using Serilog.Events;

namespace HomeAssistant.Agent.Logging;

// Reads a log file written with AgentLogging.OutputTemplate back into entries.
// This is how the desktop app shows the Windows service's log: the service
// runs in another session as another user, so there is no live channel, but
// both write the same format into the same folder.
public static partial class LogFileReader
{
    // Enough for the last few thousand lines; reading a whole 10 MB file on
    // every refresh would make the page sluggish for no benefit.
    private const int TailBytes = 1024 * 1024;

    // The most recent entries of a host across its files: rolling makes a new
    // file every day, and an earlier run is often in yesterday's. Newest files
    // first until the byte budget is spent; returned oldest first. Null when
    // the host has not written a log yet.
    public static IReadOnlyList<LogEntry>? ReadRecent(string prefix, out string? latestFile)
    {
        latestFile = null;

        if (!Directory.Exists(AgentLogging.LogsDirectory))
            return null;

        var files = new DirectoryInfo(AgentLogging.LogsDirectory)
            .GetFiles(prefix + "*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        if (files.Length == 0)
            return null;

        latestFile = files[0].FullName;

        var chunks = new List<IReadOnlyList<LogEntry>>();
        long budget = TailBytes;

        foreach (var file in files)
        {
            if (budget <= 0)
                break;

            chunks.Add(ReadTail(file.FullName, budget, out var read));
            budget -= read;
        }

        chunks.Reverse();
        return chunks.SelectMany(chunk => chunk).ToList();
    }

    public static IReadOnlyList<LogEntry> ReadTail(string path) => ReadTail(path, TailBytes, out _);

    private static IReadOnlyList<LogEntry> ReadTail(string path, long maxBytes, out long read)
    {
        string text;

        // Shared read: Serilog keeps the file open for writing (shared: true).
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var start = Math.Max(0, stream.Length - maxBytes);
            read = stream.Length - start;
            stream.Seek(start, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            text = reader.ReadToEnd();

            // Starting mid-file lands inside a line; drop that fragment.
            if (start > 0)
            {
                var firstNewLine = text.IndexOf('\n');
                text = firstNewLine < 0 ? string.Empty : text[(firstNewLine + 1)..];
            }
        }

        return Parse(text);
    }

    private static List<LogEntry> Parse(string text)
    {
        var entries = new List<LogEntry>();

        LogEntry? current = null;
        StringBuilder? continuation = null;

        void Flush()
        {
            if (current == null)
                return;

            entries.Add(continuation is { Length: > 0 }
                ? current with { Exception = continuation.ToString().TrimEnd() }
                : current);

            current = null;
            continuation = null;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var match = LineRegex().Match(line);

            if (match.Success)
            {
                Flush();

                var timestamp = DateTime.ParseExact(match.Groups["time"].Value, "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal);

                if (!LogLevels.TryParse(match.Groups["level"].Value, out var level))
                    level = LogEventLevel.Information;

                var source = match.Groups["source"].Value;
                var slash = source.IndexOf('/');

                current = new LogEntry(
                    new DateTimeOffset(timestamp),
                    level,
                    match.Groups["host"].Value,
                    source,
                    slash > 0 ? source[..slash] : null,
                    match.Groups["message"].Value,
                    null);
            }
            else if (current != null && line.Length > 0)
            {
                // Exception text and stack traces span the lines that follow.
                (continuation ??= new StringBuilder()).AppendLine(line);
            }
        }

        Flush();

        return entries;
    }

    // 2026-10-16 12:00:00.000 [Info] [svc] windows-plugin/SessionMonitor: message
    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(?<level>\w+)\] \[(?<host>\w+)\] (?<source>[^:]*): (?<message>.*)$")]
    private static partial Regex LineRegex();
}

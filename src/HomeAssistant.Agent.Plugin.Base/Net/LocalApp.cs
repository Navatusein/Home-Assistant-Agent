using System.Diagnostics;

namespace HomeAssistant.Agent.Plugin.Base.Net;

// Questions about apps on this machine that plugins talking to a local API
// all ask: is it running, and does the configured host point here at all.
public static class LocalApp
{
    // Whether any process with one of the names (without ".exe") runs.
    public static bool IsRunning(params string[] processNames) => processNames.Any(HasProcess);

    // The same for names known only on Windows. Elsewhere the app runs under
    // another name, so the answer is null (cannot tell) and the client simply
    // tries to connect instead of waiting forever for a process that never
    // shows up.
    public static bool? IsRunningOnWindows(params string[] processNames) =>
        OperatingSystem.IsWindows() ? IsRunning(processNames) : null;

    // A host that is this machine — only then does the local process list say
    // anything about the app. Empty counts as local (the default).
    public static bool IsLocalHost(string host)
    {
        return host.Length == 0
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host is "127.0.0.1" or "::1" or "[::1]"
            || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasProcess(string name)
    {
        var processes = Process.GetProcessesByName(name);

        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}

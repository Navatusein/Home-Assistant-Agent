using System.Diagnostics;
using System.Runtime.Versioning;

namespace HomeAssistant.Agent.Core.Platform.MacOS;

// The launchd daemon as the desktop app controls it. State is read here,
// unprivileged; the changes run the service executable's own verbs (install,
// start, stop, uninstall) as root, after the administrator password prompt.
[SupportedOSPlatform("macos")]
public sealed class MacServiceControl(IElevatedLauncher elevation) : IBackgroundServiceControl
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    public BackgroundServiceState GetState()
    {
        if (!File.Exists(BackgroundServiceInfo.ExecutablePath))
            return BackgroundServiceState.Missing;

        if (!File.Exists(BackgroundServiceInfo.LaunchDaemonPlist))
            return BackgroundServiceState.NotInstalled;

        // Installed but not loaded (booted out) prints nothing and fails.
        var (exitCode, output) = Launchctl("print", $"system/{BackgroundServiceInfo.LaunchdLabel}");

        if (exitCode != 0)
            return BackgroundServiceState.Stopped;

        return ParseState(output);
    }

    public async Task RunAsync(BackgroundServiceAction action)
    {
        var verb = action.ToString().ToLowerInvariant();

        var exitCode = await elevation.RunAsync(BackgroundServiceInfo.ExecutablePath, [verb]);

        if (exitCode != 0)
            throw new InvalidOperationException($"'{verb}' failed (exit code {exitCode})");
    }

    // launchctl print lists "state = running" (or "waiting", "spawn
    // scheduled", ...) for a loaded job.
    internal static BackgroundServiceState ParseState(string output)
    {
        var state = output.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("state = ", StringComparison.Ordinal))?["state = ".Length..];

        return state switch
        {
            "running" => BackgroundServiceState.Running,
            "spawn scheduled" or "xpcproxy" => BackgroundServiceState.Starting,
            "exiting" => BackgroundServiceState.Stopping,
            _ => BackgroundServiceState.Stopped,
        };
    }

    private static (int ExitCode, string Output) Launchctl(params string[] arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("/bin/launchctl", arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

            var output = process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(QueryTimeout))
                return (-1, string.Empty);

            return (process.ExitCode, output.Result);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return (-1, string.Empty);
        }
    }
}

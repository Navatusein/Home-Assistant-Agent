using System.Diagnostics;
using System.Runtime.Versioning;
using HomeAssistant.Agent.Core;
using HomeAssistant.Agent.Core.Platform.MacOS;

namespace HomeAssistant.Agent.Platform;

// The shared data folder on macOS (/Library/Application Support/
// HomeAssistantAgent) cannot be created without administrator rights. On
// Windows the installer does it; a Mac app is just copied from the disk image,
// so the first start asks once — through the service executable's "setup"
// verb, which also prepares the folder for plugins from a catalog.
//
// Runs before logging is configured: the logs live in that folder.
[SupportedOSPlatform("macos")]
internal static class MacFirstRun
{
    private const string Prompt =
        "Home Assistant Agent needs to create its data folder, which it shares with its background service.";

    // False: the agent cannot run (the user declined, or setup failed); the
    // user has been told why.
    public static bool EnsureDataFolder()
    {
        if (AgentPaths.IsPortable || CanWrite(AgentPaths.DataDirectory))
            return true;

        string? failure;

        if (!File.Exists(BackgroundServiceInfo.ExecutablePath))
        {
            failure = $"{BackgroundServiceInfo.ExecutableName} is missing next to the app. Install the app again.";
        }
        else
        {
            try
            {
                var exitCode = new MacElevatedLauncher()
                    .RunAsync(BackgroundServiceInfo.ExecutablePath, ["setup"], Prompt)
                    .GetAwaiter().GetResult();

                failure = exitCode == 0 && CanWrite(AgentPaths.DataDirectory)
                    ? null
                    : $"Creating {AgentPaths.DataDirectory} failed (exit code {exitCode}).";
            }
            catch (OperationCanceledException)
            {
                failure = "The agent keeps its settings in a folder shared with its background service, "
                    + "and creating it needs an administrator password once. Start the agent again to retry.";
            }
        }

        if (failure == null)
            return true;

        ShowAlert("Home Assistant Agent cannot start", failure);
        return false;
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return false;

            var probe = Path.Combine(directory, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);

            Directory.CreateDirectory(AgentPaths.LogsDirectory);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    // There is no window yet (nor a log), so the system's own alert.
    private static void ShowAlert(string title, string message)
    {
        try
        {
            var script = $"display alert \"{MacElevatedLauncher.EscapeForAppleScript(title)}\" "
                + $"message \"{MacElevatedLauncher.EscapeForAppleScript(message)}\" as critical";

            using var process = Process.Start(new ProcessStartInfo("/usr/bin/osascript", ["-e", script])
            {
                UseShellExecute = false,
            });

            process?.WaitForExit();
        }
        catch (Exception)
        {
            Console.Error.WriteLine($"{title}: {message}");
        }
    }
}

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using HomeAssistant.Agent.Core;

namespace HomeAssistant.Agent.Service;

// Management verbs for the launchd daemon, run as root (the desktop app asks
// for the administrator password first):
//
//   setup      create the shared data folder for the app and the daemon, and
//              the folder for plugins from a catalog — what the Windows
//              installer does for %ProgramData%. Idempotent.
//   install    setup, write the daemon's plist pointing at this executable
//              (an update or a moved app just rewrites it) and start it
//   uninstall  stop it and delete the plist
//   start      load it (if needed) and start it
//   stop       unload it and wait until it is gone, so its files can be
//              replaced right after; launchd would restart a running
//              KeepAlive job otherwise
//   status     exit code 0 = running, 1 = installed but not running,
//              2 = not installed
[SupportedOSPlatform("macos")]
internal static class LaunchdServiceInstaller
{
    private const string Label = BackgroundServiceInfo.LaunchdLabel;
    private static readonly string Target = $"system/{Label}";

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static int Run(string verb)
    {
        try
        {
            return verb.ToLowerInvariant() switch
            {
                "setup" => Setup(),
                "install" => Install(),
                "uninstall" => Uninstall(),
                "start" => Start(),
                "stop" => Stop(),
                "status" => Status(),
                _ => Unknown(verb),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    // The data folder belongs to root and the "staff" group every local user
    // is in, group-writable, with setgid so whatever appears inside stays in
    // that group; the daemon writes with umask 002 (see the plist), so the
    // app can update the files it creates too. Plugins from a catalog get
    // root's own folder: the daemon loads every dll in it.
    private static int Setup()
    {
        RequireRoot();

        var data = AgentPaths.DataDirectory;
        var logs = AgentPaths.LogsDirectory;
        var plugins = AgentPaths.InstalledPluginsDirectory;

        Directory.CreateDirectory(data);
        Directory.CreateDirectory(logs);

        // A portable copy (a development build) keeps its data in its own
        // folder: the developer's, not a system one to hand over to root.
        if (AgentPaths.IsPortable)
            return 0;

        // Files the app or the daemon wrote before are brought in line too.
        Exec("/usr/sbin/chown", "-R", "root:staff", data);
        Exec("/bin/chmod", "-R", "g+rwX", data);
        Exec("/bin/chmod", "2775", data, logs);

        if (plugins.StartsWith(data, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(plugins);
            Exec("/usr/sbin/chown", "-R", "root:wheel", plugins);
            Exec("/bin/chmod", "-R", "go-w", plugins);
            Exec("/bin/chmod", "755", plugins);
        }

        return 0;
    }

    private static int Install()
    {
        var result = Setup();

        if (result != 0)
            return result;

        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot resolve the service executable path.");

        // A loaded job keeps its old definition until it is unloaded.
        Stop();

        File.WriteAllText(BackgroundServiceInfo.LaunchDaemonPlist, BuildPlist(executable));
        Exec("/usr/sbin/chown", "root:wheel", BackgroundServiceInfo.LaunchDaemonPlist);
        Exec("/bin/chmod", "644", BackgroundServiceInfo.LaunchDaemonPlist);

        return Start();
    }

    private static int Uninstall()
    {
        Stop();

        if (File.Exists(BackgroundServiceInfo.LaunchDaemonPlist))
            File.Delete(BackgroundServiceInfo.LaunchDaemonPlist);

        return 0;
    }

    private static int Start()
    {
        RequireRoot();

        if (!File.Exists(BackgroundServiceInfo.LaunchDaemonPlist))
            return 2;

        if (!IsLoaded())
            Launchctl(throwOnError: true, "bootstrap", "system", BackgroundServiceInfo.LaunchDaemonPlist);

        Launchctl(throwOnError: true, "kickstart", Target);

        return WaitFor(IsRunning)
            ? 0
            : Fail("The service did not start within the timeout; see the service log.");
    }

    private static int Stop()
    {
        RequireRoot();

        if (!IsLoaded())
            return 0;

        Launchctl(throwOnError: false, "bootout", Target);

        return WaitFor(() => !IsLoaded())
            ? 0
            : Fail("The service did not stop within the timeout.");
    }

    private static int Status()
    {
        if (!File.Exists(BackgroundServiceInfo.LaunchDaemonPlist))
            return 2;

        return IsRunning() ? 0 : 1;
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"Unknown verb '{verb}'. Use setup | install | uninstall | start | stop | status.");
        return 1;
    }

    // RunAtLoad and KeepAlive: started at boot, restarted if it crashes, like
    // the Windows service's restart-on-failure. Umask 002 keeps the files it
    // writes group-writable for the app (see Setup). TMPDIR: launchd gives a
    // daemon none, and without it .NET cannot create named mutexes — the
    // shared log file (Serilog, shared: true) then silently writes nothing.
    // .NET reads it once at startup, so it has to come from here.
    internal static string BuildPlist(string executable) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>{Label}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{SecurityElement.Escape(executable)}</string>
          </array>
          <key>WorkingDirectory</key>
          <string>{SecurityElement.Escape(Path.GetDirectoryName(executable))}</string>
          <key>RunAtLoad</key>
          <true/>
          <key>KeepAlive</key>
          <true/>
          <key>ThrottleInterval</key>
          <integer>10</integer>
          <key>Umask</key>
          <integer>2</integer>
          <key>EnvironmentVariables</key>
          <dict>
            <key>TMPDIR</key>
            <string>/private/tmp/</string>
          </dict>
          <key>ProcessType</key>
          <string>Standard</string>
        </dict>
        </plist>
        """;

    private static bool IsLoaded() => Launchctl(throwOnError: false, "print", Target).ExitCode == 0;

    private static bool IsRunning()
    {
        var (exitCode, output) = Launchctl(throwOnError: false, "print", Target);
        return exitCode == 0 && output.Contains("state = running", StringComparison.Ordinal);
    }

    private static bool WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + StateTimeout;

        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                return false;

            Thread.Sleep(PollInterval);
        }

        return true;
    }

    private static void RequireRoot()
    {
        if (Environment.UserName != "root")
            throw new UnauthorizedAccessException("Run this command as root (the desktop app asks for the password).");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }

    private static (int ExitCode, string Output) Launchctl(bool throwOnError, params string[] arguments)
    {
        var result = Run("/bin/launchctl", arguments);

        if (throwOnError && result.ExitCode != 0)
            throw new InvalidOperationException($"launchctl {string.Join(' ', arguments)} failed: {result.Output.Trim()}");

        return result;
    }

    private static void Exec(string file, params string[] arguments)
    {
        var (exitCode, output) = Run(file, arguments);

        if (exitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(file)} {string.Join(' ', arguments)} failed: {output.Trim()}");
    }

    // Output and errors together: launchctl reports failures on either.
    private static (int ExitCode, string Output) Run(string file, string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return (process.ExitCode, output.Result + error.Result);
    }
}

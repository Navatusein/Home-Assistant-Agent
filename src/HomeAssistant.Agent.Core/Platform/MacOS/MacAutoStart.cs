using System.Runtime.Versioning;
using System.Security;

namespace HomeAssistant.Agent.Core.Platform.MacOS;

// A LaunchAgent of the current user: launchd starts the agent at sign-in.
// Desktop-only: the service is a LaunchDaemon of its own.
//
// Only the plist is written or removed. launchd reads the folder at the next
// sign-in; loading it now would start a second agent next to this one.
[SupportedOSPlatform("macos")]
public sealed class MacAutoStart : IAutoStart
{
    public const string Label = "com.navatusein.homeassistant-agent";

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", Label + ".plist");

    public void Apply(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(PlistPath);
            return;
        }

        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The path of the running agent is unknown.");

        Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
        File.WriteAllText(PlistPath, BuildPlist(executable));
    }

    // Interactive: a process with a UI, which launchd must not throttle like
    // a background job.
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
          <key>RunAtLoad</key>
          <true/>
          <key>ProcessType</key>
          <string>Interactive</string>
        </dict>
        </plist>
        """;
}

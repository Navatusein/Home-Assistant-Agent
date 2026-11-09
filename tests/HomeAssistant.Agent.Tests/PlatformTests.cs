using System.Reflection;
using HomeAssistant.Agent.Core.Platform;
using HomeAssistant.Agent.Core.Platform.MacOS;
using HomeAssistant.Agent.Core.Plugins;

// The macOS helpers below are plain text work, testable on any system.
#pragma warning disable CA1416

namespace HomeAssistant.Agent.Tests;

public class PluginPlatformsTests
{
    [Fact]
    public void A_plain_assembly_runs_anywhere()
    {
        Assert.Empty(PluginPlatforms.Of(typeof(PluginManager).Assembly));
        Assert.True(PluginPlatforms.IsSupported(null));
        Assert.True(PluginPlatforms.IsSupported([]));
    }

    // On Windows the test project targets net10.0-windows10.0.19041, so the
    // SDK marks it "Windows10.0.19041.0" — what a Windows plugin carries too.
    // Elsewhere it is a plain net10.0 assembly with no mark.
    [Fact]
    public void A_windows_assembly_is_windows_only()
    {
#if WINDOWS
        Assert.Equal(["windows"], PluginPlatforms.Of(Assembly.GetExecutingAssembly()));
#else
        Assert.Empty(PluginPlatforms.Of(Assembly.GetExecutingAssembly()));
#endif
        Assert.Equal(OperatingSystem.IsWindows(), PluginPlatforms.IsSupported(["windows"]));
        Assert.False(PluginPlatforms.IsSupported(["nosuchos"]));
    }
}

public class MacPlatformTests
{
    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("/Applications/Home Assistant Agent.app", "'/Applications/Home Assistant Agent.app'")]
    [InlineData("it's", @"'it'\''s'")]
    [InlineData("$(rm -rf /)", "'$(rm -rf /)'")]
    public void Shell_quoting_keeps_arguments_literal(string argument, string quoted)
    {
        Assert.Equal(quoted, MacElevatedLauncher.QuoteForShell(argument));
    }

    [Fact]
    public void AppleScript_escaping_keeps_the_string_closed()
    {
        Assert.Equal(@"say \""hi\"" C:\\x", MacElevatedLauncher.EscapeForAppleScript(@"say ""hi"" C:\x"));
    }

    [Fact]
    public void The_launch_agent_starts_the_app_at_sign_in()
    {
        var plist = MacAutoStart.BuildPlist("/Applications/A & B.app/Contents/MacOS/HomeAssistant.Agent");

        Assert.Contains($"<string>{MacAutoStart.Label}</string>", plist);
        Assert.Contains("<string>/Applications/A &amp; B.app/Contents/MacOS/HomeAssistant.Agent</string>", plist);
        Assert.Contains("<key>RunAtLoad</key>", plist);
        Assert.StartsWith("<?xml", plist);
    }

    [Theory]
    [InlineData("\tstate = running\n\tpid = 42\n", BackgroundServiceState.Running)]
    [InlineData("\tstate = not running\n", BackgroundServiceState.Stopped)]
    [InlineData("\tstate = spawn scheduled\n", BackgroundServiceState.Starting)]
    [InlineData("", BackgroundServiceState.Stopped)]
    public void Launchctl_state_is_read_from_its_output(string output, BackgroundServiceState state)
    {
        Assert.Equal(state, MacServiceControl.ParseState(output));
    }
}

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace HomeAssistant.Agent.Core.Platform.MacOS;

// The system's administrator password prompt, through AppleScript's
// "do shell script … with administrator privileges": the command runs as root
// once the user has confirmed.
//
// The prompt belongs to osascript, so a declined one shows in its result, not
// at start: osascript fails with error -128 ("User canceled").
[SupportedOSPlatform("macos")]
public sealed class MacElevatedLauncher : IElevatedLauncher
{
    private const string UserCanceled = "(-128)";

    // Nobody reads the output here, so it is not captured: a full pipe would
    // stall the command.
    public Process? Start(string file, IReadOnlyList<string> arguments)
    {
        var startInfo = CreateStartInfo(file, arguments);
        startInfo.RedirectStandardError = false;
        startInfo.RedirectStandardOutput = false;

        return Process.Start(startInfo);
    }

    public Task<int> RunAsync(string file, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default) =>
        RunAsync(file, arguments, prompt: null, cancellationToken);

    // prompt: what the password dialog says the app wants to do; the system's
    // generic text without it.
    public async Task<int> RunAsync(string file, IReadOnlyList<string> arguments, string? prompt,
        CancellationToken cancellationToken = default)
    {
        using var process = Process.Start(CreateStartInfo(file, arguments, prompt))
            ?? throw new InvalidOperationException("osascript did not start");

        // Both pipes drained, so a talkative command cannot fill one and stall.
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await output;

        if (process.ExitCode != 0 && (await error).Contains(UserCanceled, StringComparison.Ordinal))
            throw new OperationCanceledException("Administrator approval was declined.");

        // osascript passes the command's own failure on as exit code 1.
        return process.ExitCode;
    }

    private static ProcessStartInfo CreateStartInfo(string file, IReadOnlyList<string> arguments, string? prompt = null)
    {
        var command = string.Join(' ', new[] { file }.Concat(arguments).Select(QuoteForShell));
        var withPrompt = prompt == null ? string.Empty : $" with prompt \"{EscapeForAppleScript(prompt)}\"";
        var script = $"do shell script \"{EscapeForAppleScript(command)}\"{withPrompt} with administrator privileges";

        var startInfo = new ProcessStartInfo("/usr/bin/osascript")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(script);

        return startInfo;
    }

    // Single quotes keep everything literal in sh; a quote inside is closed,
    // escaped and reopened.
    internal static string QuoteForShell(string argument) => "'" + argument.Replace("'", "'\\''") + "'";

    public static string EscapeForAppleScript(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            if (c is '\\' or '"')
                builder.Append('\\');

            builder.Append(c);
        }

        return builder.ToString();
    }
}

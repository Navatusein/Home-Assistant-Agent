using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace HomeAssistant.Agent.Core.Platform.Windows;

// The "runas" verb: one UAC prompt per start.
[SupportedOSPlatform("windows")]
public sealed class WindowsElevatedLauncher : IElevatedLauncher
{
    // The UAC prompt was dismissed.
    private const int ErrorCancelled = 1223;

    public Process? Start(string file, IReadOnlyList<string> arguments)
    {
        try
        {
            // runas needs ShellExecute, which takes the arguments as one string.
            return Process.Start(new ProcessStartInfo(file, JoinArguments(arguments))
            {
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            throw new OperationCanceledException("Administrator approval was declined.", ex);
        }
    }

    public async Task<int> RunAsync(string file, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        using var process = Start(file, arguments)
            ?? throw new InvalidOperationException($"{Path.GetFileName(file)} did not start");

        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    // Windows command-line quoting (the rules CommandLineToArgvW parses back):
    // paths may contain spaces.
    internal static string JoinArguments(IEnumerable<string> arguments) =>
        string.Join(' ', arguments.Select(Quote));

    private static string Quote(string argument)
    {
        if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
            return argument;

        var builder = new StringBuilder("\"");
        var backslashes = 0;

        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            builder.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
            builder.Append(c);
            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2).Append('"');
        return builder.ToString();
    }
}

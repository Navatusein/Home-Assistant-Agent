using System.Diagnostics;

namespace HomeAssistant.Agent.Core.Platform;

// Starts a process with administrator rights, asking the user first.
public interface IElevatedLauncher
{
    // Starts it and returns. Arguments are passed as given; quoting is the
    // launcher's job. The caller owns the returned process, which may be null
    // when the system does not hand one out.
    //
    // Where the system asks before the process starts (UAC on Windows), a
    // declined prompt throws OperationCanceledException here. Where it asks
    // inside the started process (the password prompt on macOS), it can only
    // be told afterwards — use RunAsync when that matters.
    Process? Start(string file, IReadOnlyList<string> arguments);

    // Runs it to the end and returns its exit code. Throws
    // OperationCanceledException when the user declines, on every system.
    Task<int> RunAsync(string file, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

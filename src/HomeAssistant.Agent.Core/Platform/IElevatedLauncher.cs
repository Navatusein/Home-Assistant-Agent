using System.Diagnostics;

namespace HomeAssistant.Agent.Core.Platform;

// Starts a process with administrator rights, asking the user first.
public interface IElevatedLauncher
{
    // Arguments are passed as given; quoting is the launcher's job. Throws
    // OperationCanceledException when the user declines. The caller owns the
    // returned process, which may be null when the system does not hand one out.
    Process? Start(string file, IReadOnlyList<string> arguments);
}

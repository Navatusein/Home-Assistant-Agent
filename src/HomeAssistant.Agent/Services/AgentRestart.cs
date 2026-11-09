using System.Diagnostics;

namespace HomeAssistant.Agent.Services;

// Relaunches the agent in place.
//
// The new process performs the whole handover: it asks the running instance to
// exit through ISingleInstance and takes the session over once it is gone
// (App.TakeOverAsync). So starting it is all there is to do here — exiting
// first would leave the tray empty if that launch failed.
public static class AgentRestart
{
    // Launching the agent with this asks the copy that is already running to
    // quit, and the new process takes its place. Without a running copy it is a
    // plain start, so it is safe to use as the only launch command.
    public const string Argument = "--restart";

    public static void Start()
    {
        var path = Environment.ProcessPath
            ?? throw new InvalidOperationException("The path of the running agent is unknown.");

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = path,
            Arguments = Argument,
            // Auto-start runs the agent with System32 as the working directory,
            // and the replacement must not inherit that from us.
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
        });
    }
}

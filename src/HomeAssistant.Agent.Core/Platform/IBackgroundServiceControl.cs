namespace HomeAssistant.Agent.Core.Platform;

public enum BackgroundServiceState
{
    // The service executable is not next to the app, so nothing can be done.
    Missing,
    NotInstalled,
    Stopped,
    Starting,
    Running,
    Stopping,
    Paused,
}

public enum BackgroundServiceAction
{
    Install,
    Start,
    Stop,
    Uninstall,
}

// The agent's system-level host (the Windows service), as the desktop app
// installs and controls it.
public interface IBackgroundServiceControl
{
    BackgroundServiceState GetState();

    // Needs administrator rights, so the user is asked first. Throws
    // OperationCanceledException when they decline and
    // InvalidOperationException when the action fails.
    Task RunAsync(BackgroundServiceAction action);
}

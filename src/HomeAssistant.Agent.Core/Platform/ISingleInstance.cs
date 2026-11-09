namespace HomeAssistant.Agent.Core.Platform;

// Keeps a single desktop agent per user session and lets a second launch
// surface or replace the one already running.
public interface ISingleInstance : IDisposable
{
    // Raised on a background thread when another launch asks to be shown.
    event Action? ActivationRequested;

    // Raised on a background thread when a --restart or --shutdown launch asks
    // this instance to make way.
    event Action? ShutdownRequested;

    // True when this process became the owner. False means another instance
    // holds the session and this one should signal it and quit.
    bool TryAcquire();

    // Keeps retrying until the previous instance has released the session.
    Task<bool> TryAcquireAsync(TimeSpan timeout);

    void SignalExistingInstance();

    void SignalExistingInstanceShutdown();
}

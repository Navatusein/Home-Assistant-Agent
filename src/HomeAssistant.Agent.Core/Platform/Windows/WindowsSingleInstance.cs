using System.Runtime.Versioning;

namespace HomeAssistant.Agent.Core.Platform.Windows;

// Keeps a single agent per user session and lets a second launch surface the
// one already running.
//
// Three named kernel objects: a mutex that decides who owns the session, and
// two events a newcomer sets to ask the owner to show its window or to quit.
// Without the events a repeated launch would look like nothing happened at all
// — the window lives in the tray and the process exits silently.
//
// Session-scoped (Local\) on purpose: each logged-in user runs their own agent
// with their own MQTT identity, and the Windows service is a separate process
// that this does not touch.
[SupportedOSPlatform("windows")]
public sealed class WindowsSingleInstance : ISingleInstance
{
    private const string MutexName = @"Local\HomeAssistant.Agent.SingleInstance";
    private const string ActivateEventName = @"Local\HomeAssistant.Agent.Activate";
    private const string ShutdownEventName = @"Local\HomeAssistant.Agent.Shutdown";

    // How often the mutex is retried while waiting for the previous instance.
    private static readonly TimeSpan AcquireRetryInterval = TimeSpan.FromMilliseconds(100);

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private EventWaitHandle? _shutdownEvent;
    private RegisteredWaitHandle? _activateRegistration;
    private RegisteredWaitHandle? _shutdownRegistration;

    // Raised on a thread-pool thread when another launch asks to be shown.
    public event Action? ActivationRequested;

    // Raised on a thread-pool thread when a --restart launch asks this instance
    // to make way for it.
    public event Action? ShutdownRequested;

    // True when this process became the owner. False means another instance
    // holds the session and this one should signal it and quit.
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShutdownEventName);

        // Pooled waits rather than dedicated threads: these sit idle for the
        // entire lifetime of the application.
        _activateRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => ActivationRequested?.Invoke(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        _shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
            _shutdownEvent,
            (_, _) => ShutdownRequested?.Invoke(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        return true;
    }

    // Keeps retrying until the previous instance has released the session, so a
    // --restart launch can take it over. The wait is bounded: an instance that
    // is wedged must not turn a restart into a silent no-op.
    public async Task<bool> TryAcquireAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            if (TryAcquire())
                return true;

            if (DateTime.UtcNow >= deadline)
                return false;

            await Task.Delay(AcquireRetryInterval);
        }
    }

    public void SignalExistingInstance() => Signal(ActivateEventName);

    // Asks the running instance to shut down. It exits the same way the tray's
    // Exit does, so Home Assistant sees the device go offline rather than
    // waiting for the broker's last-will timeout.
    public void SignalExistingInstanceShutdown() => Signal(ShutdownEventName);

    private static void Signal(string eventName)
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(eventName, out var handle))
            {
                using (handle)
                    handle.Set();
            }
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The owner exited between the mutex check and this call — nothing
            // left to signal, and the caller is about to take the session over
            // (or quit) anyway.
        }
    }

    public void Dispose()
    {
        _activateRegistration?.Unregister(null);
        _activateRegistration = null;

        _shutdownRegistration?.Unregister(null);
        _shutdownRegistration = null;

        _activateEvent?.Dispose();
        _activateEvent = null;

        _shutdownEvent?.Dispose();
        _shutdownEvent = null;

        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
    }
}
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Base.Net;

// A connection to a local app that keeps itself alive: it waits for the app
// to start, connects, runs until the connection ends, and tries again after a
// delay — for as long as the plugin runs. A subclass supplies the transport
// (RunConnectionAsync) and says when the connection counts as ready
// (MarkConnected); ReconnectingWebSocketClient is the websocket one.
//
// Everything runs on one background loop; ConnectionChanged fires on it.
public abstract class ReconnectingClient : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _stateLock = new();

    private CancellationTokenSource? _attemptCts;
    private bool _waitingForApp;
    private int _started;

    protected ReconnectingClient(string appName, ILogger logger)
    {
        AppName = appName;
        Logger = logger;
    }

    // "OBS", "Logitech G HUB" — for the log lines the loop writes.
    protected string AppName { get; }

    protected ILogger Logger { get; }

    // Cancelled by Dispose: the plugin is going away.
    protected CancellationToken StoppingToken => _cts.Token;

    // Set by MarkConnected once the protocol says the connection is usable,
    // cleared when it ends.
    public bool IsConnected { get; private set; }

    public event Action? ConnectionChanged;

    // Wait between two connection attempts.
    protected virtual TimeSpan ReconnectDelay => TimeSpan.FromSeconds(10);

    // How often to look for the app while it is not running; cheap, so more
    // often than a reconnect.
    protected virtual TimeSpan AppPollInterval => TimeSpan.FromSeconds(5);

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            throw new InvalidOperationException($"The {AppName} client is already started.");

        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    public void Dispose()
    {
        _cts.Cancel();
        Dispose(true);
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }

    // Releases what the subclass holds; the loop is already cancelled.
    protected virtual void Dispose(bool disposing) { }

    // Whether the app is running, so a closed app is waited for instead of
    // producing a refused connection every few seconds. null = cannot tell
    // (a remote host): always try.
    protected virtual bool? IsAppRunning() => null;

    // One attempt: connect, then return only when the connection has ended.
    // Throwing counts as a failed attempt. The token is cancelled by Dispose
    // and by Reconnect, and stays valid for anything started on behalf of
    // this connection.
    protected abstract Task RunConnectionAsync(CancellationToken cancellationToken);

    // The attempt failed or the connection broke. Returns a different wait
    // before the next attempt, or null for the usual ReconnectDelay.
    protected virtual Task<TimeSpan?> OnConnectionFailedAsync(Exception exception)
    {
        Logger.LogDebug("{App} connection ended: {Message}", AppName, exception.Message);
        return Task.FromResult<TimeSpan?>(null);
    }

    // After every attempt, before ConnectionChanged: drop per-connection state.
    protected virtual void OnDisconnected(bool wasConnected) { }

    // Ignored once the attempt it belongs to has ended, so work left running
    // for an old connection cannot mark a dead one connected.
    protected void MarkConnected()
    {
        lock (_stateLock)
        {
            if (IsConnected || _attemptCts == null || _attemptCts.IsCancellationRequested)
                return;

            IsConnected = true;
        }

        Logger.LogInformation("Connected to {App}", AppName);
        ConnectionChanged?.Invoke();
    }

    // Drops the current connection; the loop reconnects after the usual delay.
    protected void Reconnect()
    {
        lock (_stateLock)
            _attemptCts?.Cancel();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsAppRunning() == false)
            {
                if (!_waitingForApp)
                {
                    _waitingForApp = true;
                    Logger.LogInformation("{App} is not running — waiting for it to start", AppName);
                }

                if (!await DelayAsync(AppPollInterval, cancellationToken))
                    return;

                continue;
            }

            if (_waitingForApp)
            {
                _waitingForApp = false;
                Logger.LogInformation("{App} started", AppName);
            }

            var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            lock (_stateLock)
                _attemptCts = attemptCts;

            TimeSpan? delay = null;

            try
            {
                await RunConnectionAsync(attemptCts.Token);
            }
            // Only Dispose stops the loop. Any other cancellation is a
            // Reconnect and must fall through to the reconnect delay —
            // treating it as a stop would leave the plugin dead until restart.
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                delay = await OnConnectionFailedAsync(ex);
            }
            finally
            {
                bool wasConnected;

                lock (_stateLock)
                {
                    attemptCts.Cancel();
                    _attemptCts = null;

                    wasConnected = IsConnected;
                    IsConnected = false;
                }

                attemptCts.Dispose();

                OnDisconnected(wasConnected);

                if (wasConnected)
                {
                    Logger.LogInformation("Disconnected from {App}", AppName);
                    ConnectionChanged?.Invoke();
                }
            }

            if (!await DelayAsync(delay ?? ReconnectDelay, cancellationToken))
                return;
        }
    }

    // Returns false when the wait was cancelled and the loop should stop.
    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

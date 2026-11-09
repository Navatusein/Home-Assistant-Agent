using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;

namespace HomeAssistant.Agent.Core.Platform.MacOS;

// Keeps a single agent per user and lets a second launch surface or replace
// the one already running — what named kernel objects do on Windows:
//
//   lock file    whoever holds it exclusively owns the session. .NET takes an
//                advisory flock for FileShare.None, and the system drops it
//                when the process dies, so a crash never leaves it stuck;
//   Unix socket  the owner listens; a newcomer connects and sends one line,
//                "activate" (show the window) or "shutdown" (make way).
//
// Both live in $TMPDIR, which macOS gives every user on their own: each
// signed-in user runs their own agent, and the daemon (root) is not involved.
[SupportedOSPlatform("macos")]
public sealed class MacSingleInstance : ISingleInstance
{
    private const string ActivateCommand = "activate";
    private const string ShutdownCommand = "shutdown";

    // How often the lock is retried while waiting for the previous instance.
    private static readonly TimeSpan AcquireRetryInterval = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(2);

    private readonly string _lockPath = Path.Combine(Path.GetTempPath(), "HomeAssistant.Agent.lock");
    private readonly string _socketPath = Path.Combine(Path.GetTempPath(), "HomeAssistant.Agent.sock");

    private FileStream? _lock;
    private Socket? _listener;
    private CancellationTokenSource? _listening;

    // Raised on a background thread when another launch asks to be shown.
    public event Action? ActivationRequested;

    // Raised on a background thread when a --restart or --shutdown launch asks
    // this instance to make way.
    public event Action? ShutdownRequested;

    public bool TryAcquire()
    {
        try
        {
            _lock = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return false;
        }

        // Left behind by an instance that crashed; ours from here on.
        File.Delete(_socketPath);

        _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listener.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listener.Listen(4);

        _listening = new CancellationTokenSource();
        _ = ListenAsync(_listener, _listening.Token);

        return true;
    }

    // Keeps retrying until the previous instance has released the session.
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

    public void SignalExistingInstance() => Signal(ActivateCommand);

    public void SignalExistingInstanceShutdown() => Signal(ShutdownCommand);

    public void Dispose()
    {
        _listening?.Cancel();
        _listening?.Dispose();
        _listening = null;

        if (_listener != null)
        {
            _listener.Dispose();
            _listener = null;
            File.Delete(_socketPath);
        }

        _lock?.Dispose();
        _lock = null;
    }

    private async Task ListenAsync(Socket listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket client;

            try
            {
                client = await listener.AcceptAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            using (client)
            {
                var command = await ReadCommandAsync(client);

                if (command == ActivateCommand)
                    ActivationRequested?.Invoke();
                else if (command == ShutdownCommand)
                    ShutdownRequested?.Invoke();
            }
        }
    }

    private static async Task<string?> ReadCommandAsync(Socket client)
    {
        try
        {
            using var timeout = new CancellationTokenSource(SignalTimeout);
            await using var stream = new NetworkStream(client, ownsSocket: false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            return (await reader.ReadLineAsync(timeout.Token))?.Trim();
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException)
        {
            return null;
        }
    }

    private void Signal(string command)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.SendTimeout = (int) SignalTimeout.TotalMilliseconds;
            socket.Connect(new UnixDomainSocketEndPoint(_socketPath));
            socket.Send(Encoding.UTF8.GetBytes(command + "\n"));
        }
        catch (SocketException)
        {
            // The owner exited between the lock check and this call — nothing
            // left to signal, and the caller is about to take the session over
            // (or quit) anyway.
        }
    }
}

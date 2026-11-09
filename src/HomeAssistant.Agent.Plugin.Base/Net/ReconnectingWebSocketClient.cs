using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Plugin.Base.Net;

// A websocket connection to a local app (OBS, G HUB, Teams) on top of
// ReconnectingClient's loop. A subclass only speaks the protocol: the
// address, what to send once the socket is open, how to handle a message, and
// when the connection counts as ready (MarkConnected).
//
// The message handler fires on the connection loop's background thread.
public abstract class ReconnectingWebSocketClient : ReconnectingClient
{
    private const int ReceiveBufferSize = 8192;

    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _webSocket;

    protected ReconnectingWebSocketClient(string appName, ILogger logger) : base(appName, logger)
    {
    }

    protected override void Dispose(bool disposing)
    {
        _webSocket?.Abort();
        _sendLock.Dispose();
    }

    // Built for every attempt, so it can carry state that changes in between
    // (Teams' pairing token).
    protected abstract Uri GetUri();

    // Headers and subprotocols the app insists on.
    protected virtual void ConfigureOptions(ClientWebSocketOptions options) { }

    // The socket is open. Call MarkConnected here when nothing else has to
    // happen first; protocols with a handshake call it from OnMessageAsync.
    protected virtual Task OnOpenedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected abstract Task OnMessageAsync(string message, CancellationToken cancellationToken);

    // The app closed the connection on purpose; the status may say why.
    protected virtual void OnClosedByServer(WebSocketCloseStatus? status, string? description)
    {
        Logger.LogDebug("{App} closed the connection: {Status} {Description}", AppName, (int?) status, description);
    }

    // One send at a time — handlers call this from arbitrary threads. Returns
    // false when there is no open connection to send on.
    protected async Task<bool> SendTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var webSocket = _webSocket;

        if (webSocket is not { State: WebSocketState.Open })
            return false;

        await _sendLock.WaitAsync(cancellationToken == default ? StoppingToken : cancellationToken);

        try
        {
            await webSocket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true,
                cancellationToken == default ? StoppingToken : cancellationToken);
            return true;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    protected sealed override async Task RunConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ConnectAsync(cancellationToken);
            await ReceiveLoopAsync(cancellationToken);
        }
        finally
        {
            _webSocket?.Dispose();
            _webSocket = null;
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var uri = GetUri();

        // Path only: the query may carry a token.
        Logger.LogDebug("Connecting to {App} at {Endpoint}", AppName, uri.GetLeftPart(UriPartial.Path));

        var webSocket = new ClientWebSocket();
        ConfigureOptions(webSocket.Options);
        _webSocket = webSocket;

        await webSocket.ConnectAsync(uri, cancellationToken);
        await OnOpenedAsync(cancellationToken);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferSize];
        using var message = new MemoryStream();

        while (_webSocket is { State: WebSocketState.Open } webSocket)
        {
            message.SetLength(0);

            WebSocketReceiveResult result;

            // A message may arrive in several frames.
            do
            {
                result = await webSocket.ReceiveAsync(buffer, cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    OnClosedByServer(webSocket.CloseStatus, webSocket.CloseStatusDescription);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                    return;
                }

                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            await OnMessageAsync(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int) message.Length),
                cancellationToken);
        }
    }
}

using HomeAssistant.Agent.Core.Platform;
using Microsoft.Extensions.Logging;

namespace HomeAssistant.Agent.Core.Services;

// Tells the user when the MQTT connection has been down for a while, and when
// it is back. The grace period keeps quiet through what heals by itself: every
// settings apply reconnects, and a broker restart is usually over within the
// first reconnect attempts.
public sealed class MqttConnectionNotifier
{
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(30);

    private readonly AgentService _agentService;
    private readonly IUserNotifier _notifier;
    private readonly ILogger<MqttConnectionNotifier> _logger;
    private readonly Lock _lock = new();

    private CancellationTokenSource? _pending;
    private bool _notified;

    public MqttConnectionNotifier(AgentService agentService, IUserNotifier notifier,
        ILogger<MqttConnectionNotifier> logger)
    {
        _agentService = agentService;
        _notifier = notifier;
        _logger = logger;
    }

    // Call before the first apply: a broker that is down from the start never
    // reports a drop, so the countdown starts here.
    public void Start()
    {
        _agentService.ConnectionChanged += OnConnectionChanged;
        OnConnectionChanged(false);
    }

    // Background thread.
    private void OnConnectionChanged(bool connected)
    {
        lock (_lock)
        {
            _pending?.Cancel();
            _pending = null;

            if (connected)
            {
                if (_notified)
                {
                    _notified = false;
                    _notifier.Show("MQTT connected", "The connection to the MQTT broker is back.");
                }

                return;
            }

            if (_notified)
                return;

            var pending = new CancellationTokenSource();
            _pending = pending;

            _ = NotifyAfterGraceAsync(pending.Token);
        }
    }

    private async Task NotifyAfterGraceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(GracePeriod, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_lock)
        {
            // Not configured: the settings page already says so, and there is
            // nothing to wait for.
            if (cancellationToken.IsCancellationRequested || _agentService.IsConnected || !_agentService.General.CanConnect)
                return;

            _notified = true;
        }

        var broker = _agentService.General.MqttBroker;

        _logger.LogWarning("MQTT has been disconnected for over {Seconds} s — notifying", GracePeriod.TotalSeconds);
        _notifier.Show("MQTT disconnected",
            $"No connection to the MQTT broker {broker}. Home Assistant does not see this PC — retrying in the background.",
            warning: true);
    }
}

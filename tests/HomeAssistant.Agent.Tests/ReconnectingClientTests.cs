using HomeAssistant.Agent.Plugin.Base.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace HomeAssistant.Agent.Tests;

public class ReconnectingClientTests
{
    private sealed class FakeClient(TimeSpan reconnectDelay) : ReconnectingClient("Fake", NullLogger.Instance)
    {
        private int _attempts;
        private int _disconnects;

        public bool? Running { get; set; }

        public Func<CancellationToken, Task> Connection { get; set; } =
            cancellationToken => Task.Delay(Timeout.Infinite, cancellationToken);

        public int Attempts => Volatile.Read(ref _attempts);

        public int Disconnects => Volatile.Read(ref _disconnects);

        protected override TimeSpan ReconnectDelay => reconnectDelay;

        protected override TimeSpan AppPollInterval => TimeSpan.FromMilliseconds(20);

        protected override bool? IsAppRunning() => Running;

        protected override Task RunConnectionAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);
            return Connection(cancellationToken);
        }

        protected override void OnDisconnected(bool wasConnected) => Interlocked.Increment(ref _disconnects);

        public void Connect() => MarkConnected();

        public void Drop() => Reconnect();
    }

    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20);

        Assert.True(condition());
    }

    [Fact]
    public async Task Waits_for_the_app_before_connecting()
    {
        using var client = new FakeClient(TimeSpan.FromMilliseconds(20)) { Running = false };
        client.Start();

        await Task.Delay(150);
        Assert.Equal(0, client.Attempts);

        client.Running = true;
        await Eventually(() => client.Attempts == 1);
    }

    [Fact]
    public async Task Reports_the_connection_and_reconnects_after_a_drop()
    {
        using var client = new FakeClient(TimeSpan.FromMilliseconds(20));
        var changes = 0;
        client.ConnectionChanged += () => Interlocked.Increment(ref changes);
        client.Start();

        await Eventually(() => client.Attempts == 1);
        client.Connect();
        Assert.True(client.IsConnected);

        client.Drop();

        await Eventually(() => client.Attempts == 2);
        Assert.False(client.IsConnected);
        Assert.Equal(2, Volatile.Read(ref changes));
    }

    // Work left running for a connection that has ended (a login that finishes
    // late) must not mark the client connected.
    [Fact]
    public async Task Marking_an_ended_connection_connected_is_ignored()
    {
        using var client = new FakeClient(TimeSpan.FromMinutes(1))
        {
            Connection = _ => throw new IOException("refused"),
        };
        client.Start();

        await Eventually(() => client.Disconnects == 1);
        client.Connect();

        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Dispose_stops_the_loop()
    {
        var client = new FakeClient(TimeSpan.FromMilliseconds(20));
        client.Start();

        await Eventually(() => client.Attempts == 1);
        client.Dispose();

        await Task.Delay(150);
        Assert.Equal(1, client.Attempts);
    }
}

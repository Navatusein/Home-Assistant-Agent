using System.Net;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Server;
using MqttClient = HomeAssistant.Connector.Mqtt.MqttClient;

namespace HomeAssistant.Agent.Tests;

public sealed class MqttReconnectTests
{
    private readonly int _port = FreePort();

    [Fact]
    public async Task ReconnectsAfterBrokerRestart()
    {
        var server = await StartServerAsync();

        var client = new MqttClient();
        var down = new TaskCompletionSource();
        var up = new TaskCompletionSource();

        client.ConnectionChanged += connected =>
        {
            if (!connected)
                down.TrySetResult();
            else if (down.Task.IsCompleted)
                up.TrySetResult();
        };

        Assert.True(await client.ConnectAsync(Options()));

        await server.StopAsync();
        server.Dispose();

        await down.Task.WaitAsync(TimeSpan.FromSeconds(10));

        server = await StartServerAsync();

        try
        {
            await up.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(client.IsConnected);
        }
        finally
        {
            await client.DisconnectAsync();
            await server.StopAsync();
            server.Dispose();
        }
    }

    private async Task<MqttServer> StartServerAsync()
    {
        var server = new MqttServerFactory().CreateMqttServer(new MqttServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(_port).Build());
        await server.StartAsync();
        return server;
    }

    private MqttClientOptions Options() =>
        new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", _port).Build();

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint) listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

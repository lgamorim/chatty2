using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Chatty2.Core.IntegrationTests;

public class ChatSessionIntegrationTests
{
    [Fact]
    public async Task Should_ReArmListening_When_CalledAgainRightAfterAFailedConnectCancelledThePriorAttempt()
    {
        var port = GetFreeLoopbackPort();
        var unreachablePort = GetFreeLoopbackPort();
        using var session = new ChatSession(new TcpPeerListener(), new TcpPeerConnector());

        _ = session.ListenAsync(port, CancellationToken.None);

        // Mirrors what ConnectCommand does on a failed dial: ConnectAsync cancels the
        // listen above, the dial then fails because nothing is listening on
        // unreachablePort, and listening is immediately re-armed on the same port. With
        // a real TcpListener, the first attempt's Stop() may not have released the port
        // yet - without ChatSession waiting for that teardown, this Start() could throw
        // "address already in use".
        await Assert.ThrowsAsync<SocketException>(
            () => session.ConnectAsync(IPAddress.Loopback, unreachablePort, CancellationToken.None));

        var reArmedListen = session.ListenAsync(port, CancellationToken.None);

        var connector = new TcpPeerConnector();
        using var clientConnection = await connector.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await reArmedListen.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(session.IsConnected);
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

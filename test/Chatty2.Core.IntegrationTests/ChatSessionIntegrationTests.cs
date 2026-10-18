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
        using var session = new ChatSession(new TcpPeerListener(), new TcpPeerConnector(), "local");

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

    [Fact]
    public async Task Should_ReconnectToLastDialedEndpoint_When_TheSessionEndedAndThePeerIsListeningAgain()
    {
        var port = GetFreeLoopbackPort();
        using var server = new ChatSession(new TcpPeerListener(), new TcpPeerConnector(), "server");
        using var client = new ChatSession(new TcpPeerListener(), new TcpPeerConnector(), "client");
        var serverDisconnected = new TaskCompletionSource();
        var clientDisconnected = new TaskCompletionSource();
        server.Disconnected += (_, _) => serverDisconnected.TrySetResult();
        client.Disconnected += (_, _) => clientDisconnected.TrySetResult();

        var firstListen = server.ListenAsync(port, CancellationToken.None);
        await client.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await firstListen.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        client.Disconnect();
        await Task.WhenAll(serverDisconnected.Task, clientDisconnected.Task)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Mirrors /redial: the app re-arms listening after a disconnect, and the dialing
        // side reuses the endpoint the session remembered instead of being retyped.
        var secondListen = server.ListenAsync(port, CancellationToken.None);
        var endpoint = client.LastDialedEndpoint;
        Assert.NotNull(endpoint);
        await client.ConnectAsync(endpoint.Address, endpoint.Port, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await secondListen.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(client.IsConnected);
        Assert.True(server.IsConnected);
    }

    [Fact]
    public async Task Should_ReportLocalRequestToTheCaller_AndPeerClosedWithItsNameToTheOtherSide_When_OneSideDisconnects()
    {
        var port = GetFreeLoopbackPort();
        using var server = new ChatSession(new TcpPeerListener(), new TcpPeerConnector(), "server");
        using var client = new ChatSession(new TcpPeerListener(), new TcpPeerConnector(), "client");
        var serverDisconnected = new TaskCompletionSource<DisconnectedEventArgs>();
        var clientDisconnected = new TaskCompletionSource<DisconnectedEventArgs>();
        server.Disconnected += (_, e) => serverDisconnected.TrySetResult(e);
        client.Disconnected += (_, e) => clientDisconnected.TrySetResult(e);

        var listen = server.ListenAsync(port, CancellationToken.None);
        await client.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await listen.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        client.Disconnect();
        var clientSide = await clientDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var serverSide = await serverDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(DisconnectReason.LocalRequest, clientSide.Reason);
        // The client's name line precedes its end of stream on the wire, so the server has
        // always read it by the time the stream ends.
        Assert.Equal(DisconnectReason.PeerClosed, serverSide.Reason);
        Assert.Equal("client", serverSide.PeerName);
    }

    [Fact]
    public async Task Should_ReportConnectionLost_When_TheRemoteResetsTheConnection()
    {
        var port = GetFreeLoopbackPort();
        using var session = new ChatSession(new TcpPeerListener(), new TcpPeerConnector(), "local");
        var connected = new TaskCompletionSource();
        var disconnected = new TaskCompletionSource<DisconnectedEventArgs>();
        session.PeerConnected += (_, _) => connected.TrySetResult();
        session.Disconnected += (_, e) => disconnected.TrySetResult(e);

        _ = session.ListenAsync(port, CancellationToken.None);
        using var rawClient = new TcpClient();
        await rawClient.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // A zero linger timeout makes closing send a reset instead of an orderly FIN, which
        // is how a crashed peer or a dropped link looks to the session.
        rawClient.LingerState = new LingerOption(true, 0);
        rawClient.Dispose();
        var args = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(DisconnectReason.ConnectionLost, args.Reason);
    }

    [Fact]
    public async Task Should_DropOversizedLineAndKeepTheSessionUp_When_PeerSendsOne()
    {
        var port = GetFreeLoopbackPort();
        using var session = new ChatSession(new TcpPeerListener(), new TcpPeerConnector(), "local");
        var dropped = new TaskCompletionSource<MessageDroppedEventArgs>();
        var received = new TaskCompletionSource<string>();
        session.MessageDropped += (_, e) => dropped.TrySetResult(e);
        session.MessageReceived += (_, e) => received.TrySetResult(e.Message);

        var listen = session.ListenAsync(port, CancellationToken.None);

        // A bare TcpPeerConnection is a peer that skips the sender-side guard, as an older
        // or hostile client would.
        using var rawPeer = await new TcpPeerConnector().ConnectAsync(IPAddress.Loopback, port, CancellationToken.None);
        await listen.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await rawPeer.SendAsync("NAME:Raw", CancellationToken.None);
        await rawPeer.SendAsync(new string('x', ChatSession.MaxMessageLength * 3), CancellationToken.None);
        await rawPeer.SendAsync("still here", CancellationToken.None);

        var notice = await dropped.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(ChatSession.MaxMessageLength, notice.MaxLength);
        Assert.Equal("still here", message);
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

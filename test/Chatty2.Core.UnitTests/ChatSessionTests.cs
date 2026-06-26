using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Chatty2.Core;
using NSubstitute;
using Xunit;

namespace Chatty2.Core.UnitTests;

public class ChatSessionTests
{
    [Fact]
    public async Task Should_RaisePeerConnected_When_ListenAsync_AcceptsAConnection()
    {
        var listener = Substitute.For<IPeerListener>();
        var connection = CreatePendingConnection("10.0.0.5:53000");
        listener.AcceptAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(connection));

        var session = new ChatSession(listener, Substitute.For<IPeerConnector>());
        PeerConnectedEventArgs? raisedArgs = null;
        session.PeerConnected += (_, e) => raisedArgs = e;

        await session.ListenAsync(ChatSession.DefaultPort, CancellationToken.None);

        Assert.NotNull(raisedArgs);
        Assert.Equal("10.0.0.5:53000", raisedArgs!.RemoteEndPoint);
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task Should_RaisePeerConnected_When_ConnectAsync_Succeeds()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = CreatePendingConnection("192.168.1.10:53000");
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector);
        PeerConnectedEventArgs? raisedArgs = null;
        session.PeerConnected += (_, e) => raisedArgs = e;

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        Assert.NotNull(raisedArgs);
        Assert.Equal("192.168.1.10:53000", raisedArgs!.RemoteEndPoint);
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task Should_ThrowInvalidOperationExceptionAndDisposeCandidate_When_ConnectAsync_CalledWhileAlreadyConnected()
    {
        var connector = Substitute.For<IPeerConnector>();
        var firstConnection = CreatePendingConnection("first");
        var secondConnection = Substitute.For<IPeerConnection>();

        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(firstConnection), Task.FromResult(secondConnection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector);
        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None));

        secondConnection.Received(1).Dispose();
        firstConnection.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task Should_DisposeCandidateAndKeepListening_When_ListenAsync_AcceptsAnotherPeerWhileAlreadyConnected()
    {
        var listener = Substitute.For<IPeerListener>();
        var connector = Substitute.For<IPeerConnector>();
        var firstConnection = CreatePendingConnection("first");
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(firstConnection));

        var session = new ChatSession(listener, connector);
        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        var secondConnection = Substitute.For<IPeerConnection>();
        using var cts = new CancellationTokenSource();
        var acceptCallCount = 0;
        listener.AcceptAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                acceptCallCount++;
                if (acceptCallCount == 1) return Task.FromResult(secondConnection);

                cts.Cancel();
                return Task.FromCanceled<IPeerConnection>(cts.Token);
            });

        // Same session, already connected to firstConnection: a second accept must be
        // rejected (disposed) and the listen loop must keep going until cancelled.
        await session.ListenAsync(ChatSession.DefaultPort, cts.Token).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        secondConnection.Received(1).Dispose();
        firstConnection.DidNotReceive().Dispose();
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task Should_StopAndReturn_When_CancellationRequested_BeforeAnyConnectionAccepted()
    {
        var listener = Substitute.For<IPeerListener>();
        listener.AcceptAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => Task.FromCanceled<IPeerConnection>(callInfo.ArgAt<CancellationToken>(1)));

        var session = new ChatSession(listener, Substitute.For<IPeerConnector>());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await session.ListenAsync(ChatSession.DefaultPort, cts.Token).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task Should_CancelListening_When_ConnectAsync_IsCalled()
    {
        var listener = Substitute.For<IPeerListener>();
        var connector = Substitute.For<IPeerConnector>();

        CancellationToken capturedToken = default;
        var acceptTcs = new TaskCompletionSource<IPeerConnection>();
        listener.AcceptAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedToken = callInfo.ArgAt<CancellationToken>(1);
                return acceptTcs.Task;
            });

        var connectedConnection = CreatePendingConnection("peer");
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connectedConnection));

        var session = new ChatSession(listener, connector);
        var listenTask = session.ListenAsync(ChatSession.DefaultPort, CancellationToken.None);

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        Assert.True(capturedToken.IsCancellationRequested);

        acceptTcs.TrySetCanceled(capturedToken);
        await listenTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Should_RaiseMessageReceivedInOrder_When_MultipleLinesReceived()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = Substitute.For<IPeerConnection>();
        connection.RemoteEndPoint.Returns("peer");
        connection.ReceiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("hello"), Task.FromResult<string?>("world"), Task.FromResult<string?>(null));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector);
        var received = new List<string>();
        var disconnectedTcs = new TaskCompletionSource();
        session.MessageReceived += (_, e) => received.Add(e.Message);
        session.Disconnected += (_, _) => disconnectedTcs.TrySetResult();

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);
        await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(["hello", "world"], received);
    }

    [Fact]
    public async Task Should_RaiseDisconnectedAndStopLoop_When_ReceiveAsync_ReturnsNull()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = Substitute.For<IPeerConnection>();
        connection.RemoteEndPoint.Returns("peer");
        connection.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector);
        var disconnectedTcs = new TaskCompletionSource();
        session.Disconnected += (_, _) => disconnectedTcs.TrySetResult();

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);
        await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task Should_RaiseDisconnectedAndStopLoop_When_ReceiveAsync_Throws()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = Substitute.For<IPeerConnection>();
        connection.RemoteEndPoint.Returns("peer");
        connection.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException<string?>(new IOException("dropped")));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector);
        var disconnectedTcs = new TaskCompletionSource();
        session.Disconnected += (_, _) => disconnectedTcs.TrySetResult();

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);
        await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task Should_ThrowInvalidOperationException_When_SendAsync_CalledWhileNotConnected()
    {
        var session = new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync("hi", CancellationToken.None));
    }

    [Fact]
    public async Task Should_PropagateException_When_UnderlyingSendAsync_Throws()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = CreatePendingConnection("peer");
        connection.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("broken")));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector);
        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => session.SendAsync("hi", CancellationToken.None));
    }

    [Fact]
    public void Should_ThrowInvalidOperationException_When_Disconnect_CalledWhileNotConnected()
    {
        var session = new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>());

        Assert.Throws<InvalidOperationException>(() => session.Disconnect());
    }

    [Fact]
    public async Task Should_RaiseDisconnectedAndDisposeConnection_When_Disconnect_Called()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = Substitute.For<IPeerConnection>();
        connection.RemoteEndPoint.Returns("peer");
        var receiveTcs = new TaskCompletionSource<string?>();
        connection.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(receiveTcs.Task);
        connection.When(c => c.Dispose()).Do(_ => receiveTcs.TrySetException(new ObjectDisposedException(nameof(connection))));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector);
        var disconnectedTcs = new TaskCompletionSource();
        session.Disconnected += (_, _) => disconnectedTcs.TrySetResult();

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        session.Disconnect();

        await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        // Disconnect() disposes the connection directly, and the receive loop's own
        // cleanup (triggered by the resulting failed ReceiveAsync) disposes it again -
        // both calls are expected since IPeerConnection.Dispose() must be idempotent.
        connection.Received().Dispose();
        Assert.False(session.IsConnected);
    }

    [Fact]
    public void Should_NotThrow_When_Dispose_CalledMultipleTimes()
    {
        var session = new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>());

        session.Dispose();
        session.Dispose();
    }

    [Fact]
    public async Task Should_DisposeActiveConnection_When_SessionDisposed()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = CreatePendingConnection("peer");
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector);
        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        session.Dispose();

        connection.Received(1).Dispose();
    }

    private static IPeerConnection CreatePendingConnection(string remoteEndPoint)
    {
        var connection = Substitute.For<IPeerConnection>();
        connection.RemoteEndPoint.Returns(remoteEndPoint);
        connection.ReceiveAsync(Arg.Any<CancellationToken>()).Returns(new TaskCompletionSource<string?>().Task);
        return connection;
    }
}

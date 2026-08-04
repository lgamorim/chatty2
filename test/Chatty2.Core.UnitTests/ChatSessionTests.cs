using System.Net;
using System.Net.Sockets;
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

        var session = new ChatSession(listener, Substitute.For<IPeerConnector>(), "local");
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

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
        PeerConnectedEventArgs? raisedArgs = null;
        session.PeerConnected += (_, e) => raisedArgs = e;

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        Assert.NotNull(raisedArgs);
        Assert.Equal("192.168.1.10:53000", raisedArgs!.RemoteEndPoint);
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task Should_SendHandshakeWithLocalUserName_When_ConnectAsync_Succeeds()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = CreatePendingConnection("192.168.1.10:53000");
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "Alice");

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        await connection.Received(1).SendAsync(Arg.Is<string>(s => s.Contains("Alice")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_SendHandshakeWithLocalUserName_When_ListenAsync_AcceptsAConnection()
    {
        var listener = Substitute.For<IPeerListener>();
        var connection = CreatePendingConnection("10.0.0.5:53000");
        listener.AcceptAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(connection));

        var session = new ChatSession(listener, Substitute.For<IPeerConnector>(), "Bob");

        await session.ListenAsync(ChatSession.DefaultPort, CancellationToken.None);

        await connection.Received(1).SendAsync(Arg.Is<string>(s => s.Contains("Bob")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReleaseConnectionAndPropagate_When_HandshakeSendFailsAfterConnectAsync()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = Substitute.For<IPeerConnection>();
        connection.RemoteEndPoint.Returns("peer");
        connection.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("broken")));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");

        await Assert.ThrowsAsync<IOException>(
            () => session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None));

        // The failed handshake must not leave the slot permanently claimed - otherwise every
        // future ConnectAsync/SendAsync call would keep failing until the app restarts.
        Assert.False(session.IsConnected);
        connection.Received(1).Dispose();
    }

    [Fact]
    public async Task Should_KeepListening_When_HandshakeSendFailsRightAfterAcceptingAConnection()
    {
        var listener = Substitute.For<IPeerListener>();
        var failingConnection = Substitute.For<IPeerConnection>();
        failingConnection.RemoteEndPoint.Returns("bad-peer");
        failingConnection.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("broken")));
        var goodConnection = CreatePendingConnection("good-peer");

        var acceptCallCount = 0;
        listener.AcceptAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                acceptCallCount++;
                return Task.FromResult(acceptCallCount == 1 ? failingConnection : goodConnection);
            });

        var session = new ChatSession(listener, Substitute.For<IPeerConnector>(), "local");
        var listenFailedRaised = false;
        session.ListenFailed += (_, _) => listenFailedRaised = true;

        // A failed handshake right after accepting must be treated as a single stillborn
        // peer, not a listener-level failure - otherwise every real caller (which invokes
        // ListenAsync fire-and-forget) would see listening die silently on the first peer
        // that connects and drops before finishing the handshake.
        await session.ListenAsync(ChatSession.DefaultPort, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, acceptCallCount);
        Assert.True(session.IsConnected);
        Assert.False(listenFailedRaised);
        failingConnection.Received(1).Dispose();
    }

    [Fact]
    public void Should_ThrowArgumentNullException_When_LocalUserNameIsNull() =>
        Assert.Throws<ArgumentNullException>(
            () => new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>(), null!));

    [Fact]
    public void Should_ThrowArgumentException_When_LocalUserNameIsEmpty() =>
        Assert.Throws<ArgumentException>(
            () => new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>(), ""));

    [Fact]
    public void Should_ThrowArgumentException_When_LocalUserNameContainsNewline() =>
        Assert.Throws<ArgumentException>(
            () => new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>(), "Alice\nfake message"));

    [Fact]
    public void Should_ThrowArgumentException_When_LocalUserNameContainsCarriageReturn() =>
        Assert.Throws<ArgumentException>(
            () => new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>(), "Alice\rfake message"));

    [Fact]
    public void Should_ThrowArgumentException_When_LocalUserNameExceedsMaxLength() =>
        Assert.Throws<ArgumentException>(
            () => new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>(), new string('a', 65)));

    [Fact]
    public async Task Should_TruncateOverlongPeerUserName_When_HandshakeExceedsMaxLength()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = Substitute.For<IPeerConnection>();
        connection.RemoteEndPoint.Returns("peer");
        connection.ReceiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("NAME:" + new string('a', 100)), Task.FromResult<string?>(null));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
        PeerIdentifiedEventArgs? identified = null;
        var disconnectedTcs = new TaskCompletionSource();
        session.PeerIdentified += (_, e) => identified = e;
        session.Disconnected += (_, _) => disconnectedTcs.TrySetResult();

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);
        await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.NotNull(identified);
        Assert.Equal(64, identified!.UserName.Length);
    }

    [Fact]
    public async Task Should_NotSplitASurrogatePair_When_TruncatingAnOverlongPeerUserName()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = Substitute.For<IPeerConnection>();
        connection.RemoteEndPoint.Returns("peer");
        // 63 'a's (indices 0-62) plus a non-BMP character (a UTF-16 surrogate pair at
        // indices 63-64) is 65 UTF-16 code units - one over the cap, with the cut landing
        // exactly between the emoji's high and low surrogate.
        var overlongName = new string('a', 63) + "\U0001F600";
        connection.ReceiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("NAME:" + overlongName), Task.FromResult<string?>(null));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
        PeerIdentifiedEventArgs? identified = null;
        var disconnectedTcs = new TaskCompletionSource();
        session.PeerIdentified += (_, e) => identified = e;
        session.Disconnected += (_, _) => disconnectedTcs.TrySetResult();

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);
        await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.NotNull(identified);
        // A naive cut at the 64-code-unit boundary would keep the emoji's high surrogate
        // and drop its low surrogate, leaving an ill-formed string. Backing off to drop the
        // whole surrogate pair instead keeps the result well-formed.
        Assert.Equal(new string('a', 63), identified!.UserName);
    }

    [Fact]
    public async Task Should_ForwardAsMessageReceived_When_FirstReceivedLineIsNotAHandshake()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = Substitute.For<IPeerConnection>();
        connection.RemoteEndPoint.Returns("peer");
        connection.ReceiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("not a handshake line"), Task.FromResult<string?>(null));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
        var received = new List<string>();
        var disconnectedTcs = new TaskCompletionSource();
        session.MessageReceived += (_, e) => received.Add(e.Message);
        session.Disconnected += (_, _) => disconnectedTcs.TrySetResult();

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);
        await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(["not a handshake line"], received);
    }

    [Fact]
    public async Task Should_RaisePeerIdentifiedAndNotForwardIt_When_FirstReceivedLineIsHandshake()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = Substitute.For<IPeerConnection>();
        connection.RemoteEndPoint.Returns("peer");
        // "NAME:" mirrors ChatSession's internal handshake-line convention.
        connection.ReceiveAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("NAME:Bob"), Task.FromResult<string?>("hello"), Task.FromResult<string?>(null));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "Alice");
        var received = new List<string>();
        PeerIdentifiedEventArgs? identified = null;
        var disconnectedTcs = new TaskCompletionSource();
        session.MessageReceived += (_, e) => received.Add(e.Message);
        session.PeerIdentified += (_, e) => identified = e;
        session.Disconnected += (_, _) => disconnectedTcs.TrySetResult();

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);
        await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.NotNull(identified);
        Assert.Equal("Bob", identified!.UserName);
        Assert.Equal(["hello"], received);
    }

    [Fact]
    public async Task Should_ThrowInvalidOperationExceptionWithoutDialing_When_ConnectAsync_CalledWhileAlreadyConnected()
    {
        var connector = Substitute.For<IPeerConnector>();
        var firstConnection = CreatePendingConnection("first");

        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(firstConnection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None));

        // The second call must short-circuit before dialing out at all - otherwise the
        // target peer would see a stray connect immediately followed by a disconnect.
        await connector.Received(1).ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        firstConnection.DidNotReceive().Dispose();
    }

    [Fact]
    public async Task Should_RejectWhicheverClaimsSecond_When_BothConnectAsyncCallsDialConcurrentlyBeforeEitherClaims()
    {
        var connector = Substitute.For<IPeerConnector>();
        var firstConnectTcs = new TaskCompletionSource<IPeerConnection>();
        var secondConnectTcs = new TaskCompletionSource<IPeerConnection>();
        var firstConnection = CreatePendingConnection("first");
        var secondConnection = Substitute.For<IPeerConnection>();
        var dialCallCount = 0;

        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                dialCallCount++;
                return dialCallCount == 1 ? firstConnectTcs.Task : secondConnectTcs.Task;
            });

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");

        // Both calls pass the early "not connected yet" check and start dialing before
        // either has claimed the slot - the rare genuinely-concurrent /connect race the
        // early check (added for the common sequential case above) can't catch on its
        // own. Claim() is still the final backstop that resolves it correctly.
        var firstCall = session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);
        var secondCall = session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);
        Assert.Equal(2, dialCallCount);

        firstConnectTcs.SetResult(firstConnection);
        await firstCall;

        secondConnectTcs.SetResult(secondConnection);
        await Assert.ThrowsAsync<InvalidOperationException>(() => secondCall);

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

        var session = new ChatSession(listener, connector, "local");
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

        var session = new ChatSession(listener, Substitute.For<IPeerConnector>(), "local");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await session.ListenAsync(ChatSession.DefaultPort, cts.Token).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task Should_SupportRepeatedReArming_When_ListenAsync_CalledManyTimesInSuccession()
    {
        // Each call creates a new linked CancellationTokenSource and disposes the
        // previous one; calling this many times in a row (as a long-lived session with
        // several reconnects would) must keep working without leaking into a broken
        // state or throwing on an already-disposed source.
        var listener = Substitute.For<IPeerListener>();
        var acceptCallCount = 0;
        listener.AcceptAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                acceptCallCount++;
                return Task.FromCanceled<IPeerConnection>(callInfo.ArgAt<CancellationToken>(1));
            });

        var session = new ChatSession(listener, Substitute.For<IPeerConnector>(), "local");

        // Cancellation happens during each accept (not before calling ListenAsync) so
        // the loop actually reaches AcceptAsync every time, exercising the
        // create-then-dispose-previous CTS path on each re-arm.
        for (var i = 0; i < 5; i++)
        {
            await session.ListenAsync(ChatSession.DefaultPort, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        Assert.Equal(5, acceptCallCount);
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

        var session = new ChatSession(listener, connector, "local");
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

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
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

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
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

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
        var disconnectedTcs = new TaskCompletionSource();
        session.Disconnected += (_, _) => disconnectedTcs.TrySetResult();

        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);
        await disconnectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task Should_RaiseListenFailedAndNotFault_When_AcceptAsync_ThrowsNonCancellationException()
    {
        var listener = Substitute.For<IPeerListener>();
        var failure = new SocketException();
        listener.AcceptAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IPeerConnection>(failure));

        var session = new ChatSession(listener, Substitute.For<IPeerConnector>(), "local");
        ListenFailedEventArgs? raisedArgs = null;
        session.ListenFailed += (_, e) => raisedArgs = e;

        // Must complete normally (not fault) even though AcceptAsync threw - every real
        // caller invokes ListenAsync fire-and-forget, so a faulted task here would
        // otherwise go unobserved and silently kill listening.
        await session.ListenAsync(ChatSession.DefaultPort, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.NotNull(raisedArgs);
        Assert.Same(failure, raisedArgs!.Exception);
        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task Should_WaitForPriorAttemptToFinish_When_ListenAsync_CalledAgainWhilePriorAttemptStillPending()
    {
        var listener = Substitute.For<IPeerListener>();
        var firstAcceptTcs = new TaskCompletionSource<IPeerConnection>();
        var secondConnection = CreatePendingConnection("peer");
        var acceptCallCount = 0;

        listener.AcceptAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                acceptCallCount++;
                return acceptCallCount == 1 ? firstAcceptTcs.Task : Task.FromResult(secondConnection);
            });

        var session = new ChatSession(listener, Substitute.For<IPeerConnector>(), "local");

        var firstListen = session.ListenAsync(ChatSession.DefaultPort, CancellationToken.None);
        var secondListen = session.ListenAsync(ChatSession.DefaultPort, CancellationToken.None);

        // The second attempt must wait for the first attempt's TcpListener to finish
        // tearing down (its own AcceptAsync call to complete) rather than racing it by
        // calling AcceptAsync again immediately.
        Assert.Equal(1, acceptCallCount);

        firstAcceptTcs.TrySetCanceled(TestContext.Current.CancellationToken);
        await firstListen.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await secondListen.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(2, acceptCallCount);
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task Should_ThrowInvalidOperationException_When_SendAsync_CalledWhileNotConnected()
    {
        var session = new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>(), "local");

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SendAsync("hi", CancellationToken.None));
    }

    [Fact]
    public async Task Should_PropagateException_When_UnderlyingSendAsync_Throws()
    {
        var connector = Substitute.For<IPeerConnector>();
        var connection = CreatePendingConnection("peer");
        // Scoped to "hi" specifically so the handshake send ClaimAsync issues on connect
        // (a SendAsync call of its own) isn't what throws.
        connection.SendAsync(Arg.Is("hi"), Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("broken")));
        connector.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(connection));

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
        await session.ConnectAsync(IPAddress.Loopback, 53000, CancellationToken.None);

        await Assert.ThrowsAsync<IOException>(() => session.SendAsync("hi", CancellationToken.None));
    }

    [Fact]
    public void Should_ThrowInvalidOperationException_When_Disconnect_CalledWhileNotConnected()
    {
        var session = new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>(), "local");

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

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
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
        var session = new ChatSession(Substitute.For<IPeerListener>(), Substitute.For<IPeerConnector>(), "local");

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

        var session = new ChatSession(Substitute.For<IPeerListener>(), connector, "local");
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

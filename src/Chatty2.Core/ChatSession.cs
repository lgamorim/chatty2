using System.Net;

namespace Chatty2.Core;

public sealed class ChatSession(IPeerListener listener, IPeerConnector connector, string localUserName) : IChatSession
{
    public const int DefaultPort = 53000;
    private const string HandshakePrefix = "NAME:";
    private const int MaxUserNameLength = 64;

    private readonly string _localUserName = ValidateLocalUserName(localUserName);
    private readonly Lock _gate = new();
    private IPeerConnection? _activeConnection;
    private CancellationTokenSource? _listenCts;
    private Task _previousListenAttempt = Task.CompletedTask;
    private bool _disposed;

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _activeConnection is not null;
            }
        }
    }

    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    public event EventHandler<PeerConnectedEventArgs>? PeerConnected;

    public event EventHandler<PeerIdentifiedEventArgs>? PeerIdentified;

    public event EventHandler? Disconnected;

    public event EventHandler<ListenFailedEventArgs>? ListenFailed;

    public Task ListenAsync(int port, CancellationToken cancellationToken)
    {
        Task priorAttempt;
        lock (_gate)
        {
            priorAttempt = _previousListenAttempt;
        }

        var attempt = ListenCoreAsync(port, cancellationToken, priorAttempt);

        lock (_gate)
        {
            _previousListenAttempt = attempt;
        }

        return attempt;
    }

    private async Task ListenCoreAsync(int port, CancellationToken cancellationToken, Task priorAttempt)
    {
        // A re-arm (e.g. right after a failed /connect, or after a disconnect) can be
        // requested before the previous attempt's TcpListener has finished releasing the
        // port in its own teardown. Waiting for it here avoids racing that release and
        // binding too early.
        await WaitWithoutThrowingAsync(priorAttempt);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenSource? previousCts;
        lock (_gate)
        {
            previousCts = _listenCts;
            _listenCts = cts;
        }

        // The attempt that owned previousCts has already completed by this point (we just
        // awaited it above), so it's no longer in use - safe to dispose without yanking a
        // token out from under a live AcceptAsync call.
        previousCts?.Dispose();

        var token = cts.Token;

        while (!token.IsCancellationRequested)
        {
            IPeerConnection candidate;
            try
            {
                candidate = await listener.AcceptAsync(port, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                // Every caller invokes ListenAsync fire-and-forget, so any exception that
                // escapes here would otherwise go unobserved and listening would die
                // silently. Surface it instead so the failure is visible and recoverable.
                ListenFailed?.Invoke(this, new ListenFailedEventArgs(exception));
                return;
            }

            try
            {
                await ClaimAsync(candidate);
                return;
            }
            catch (InvalidOperationException)
            {
                // Someone else claimed the active connection slot concurrently (the rare
                // simultaneous-connect race). The candidate was already disposed by Claim;
                // keep listening for a legitimate future peer.
            }
            catch (Exception)
            {
                // The handshake send failed right after accepting (peer connected and
                // dropped immediately, RST, socket closed mid-handshake). ClaimAsync has
                // already released the claimed slot and disposed the candidate; treat this
                // as a single stillborn peer rather than a listener-level failure.
            }
        }
    }

    private static async Task WaitWithoutThrowingAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // A prior listen attempt's own failure is already surfaced via ListenFailed;
            // it must not fault this attempt too.
        }
    }

    public async Task ConnectAsync(IPAddress ipAddress, int port, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        CancellationTokenSource? cts;
        lock (_gate)
        {
            // Fail fast before tearing down listening or dialing out: Claim would reject
            // this anyway once the candidate connection comes back, but only after the
            // target peer has already seen a connect followed immediately by a disconnect.
            if (_activeConnection is not null)
                throw new InvalidOperationException("Already connected to a peer.");

            cts = _listenCts;
        }

        cts?.Cancel();

        var candidate = await connector.ConnectAsync(ipAddress, port, cancellationToken);
        await ClaimAsync(candidate);
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        IPeerConnection connection;
        lock (_gate)
        {
            connection = _activeConnection ?? throw new InvalidOperationException("Not connected to a peer.");
        }

        return connection.SendAsync(message, cancellationToken);
    }

    public void Disconnect()
    {
        IPeerConnection connection;
        lock (_gate)
        {
            connection = _activeConnection ?? throw new InvalidOperationException("Not connected to a peer.");
        }

        connection.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _listenCts;
            _listenCts = null;
            _activeConnection?.Dispose();
            _activeConnection = null;
        }

        cts?.Cancel();
        cts?.Dispose();
    }

    private async Task ClaimAsync(IPeerConnection candidate)
    {
        lock (_gate)
        {
            if (_activeConnection is not null)
            {
                candidate.Dispose();
                throw new InvalidOperationException("Already connected to a peer.");
            }

            _activeConnection = candidate;
        }

        try
        {
            // Sent before PeerConnected fires and before control returns to whichever caller
            // triggered this (ConnectAsync, or the accept loop in ListenCoreAsync). Neither
            // caller lets a user-typed message reach SendAsync before that point, so this
            // line is always the first thing the peer sees on this connection.
            await candidate.SendAsync(FormatHandshake(_localUserName), CancellationToken.None);
        }
        catch
        {
            // The slot was already claimed above; a failed handshake must release it again,
            // otherwise the session is stuck "connected" to a candidate that never finished
            // connecting and every future ConnectAsync/SendAsync call fails until restart.
            lock (_gate)
            {
                if (ReferenceEquals(_activeConnection, candidate)) _activeConnection = null;
            }

            candidate.Dispose();
            throw;
        }

        PeerConnected?.Invoke(this, new PeerConnectedEventArgs(candidate.RemoteEndPoint));
        _ = ReceiveLoopAsync(candidate);
    }

    private static string ValidateLocalUserName(string userName)
    {
        ArgumentNullException.ThrowIfNull(userName);

        if (userName.Length is 0 or > MaxUserNameLength || userName.Contains('\r') || userName.Contains('\n'))
        {
            throw new ArgumentException(
                $"User name must be 1-{MaxUserNameLength} characters and must not contain line breaks.",
                nameof(userName));
        }

        return userName;
    }

    private static string FormatHandshake(string userName) => HandshakePrefix + userName;

    private static bool TryParseHandshake(string line, out string userName)
    {
        if (line.StartsWith(HandshakePrefix, StringComparison.Ordinal))
        {
            var parsed = line[HandshakePrefix.Length..];
            // The peer's own ChatSession enforces this bound on its own local name, but a
            // non-conforming or hostile peer could still send something longer - cap what
            // reaches the terminal via ConsoleAppRunner's "[{name}] ..." label.
            userName = parsed.Length > MaxUserNameLength ? parsed[..MaxUserNameLength] : parsed;
            return true;
        }

        userName = "";
        return false;
    }

    private async Task ReceiveLoopAsync(IPeerConnection connection)
    {
        try
        {
            var isFirstMessage = true;

            while (true)
            {
                string? message;
                try
                {
                    message = await connection.ReceiveAsync(CancellationToken.None);
                }
                catch (Exception)
                {
                    break;
                }

                if (message is null) break;

                if (isFirstMessage)
                {
                    isFirstMessage = false;
                    if (TryParseHandshake(message, out var peerUserName))
                    {
                        PeerIdentified?.Invoke(this, new PeerIdentifiedEventArgs(peerUserName));
                        continue;
                    }
                }

                MessageReceived?.Invoke(this, new ChatMessageReceivedEventArgs(message));
            }
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_activeConnection, connection)) _activeConnection = null;
            }

            connection.Dispose();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}

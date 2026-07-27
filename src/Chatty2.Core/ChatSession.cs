using System.Net;

namespace Chatty2.Core;

public sealed class ChatSession(IPeerListener listener, IPeerConnector connector) : IChatSession
{
    public const int DefaultPort = 53000;

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
                Claim(candidate);
                return;
            }
            catch (InvalidOperationException)
            {
                // Someone else claimed the active connection slot concurrently (the rare
                // simultaneous-connect race). The candidate was already disposed by Claim;
                // keep listening for a legitimate future peer.
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
        Claim(candidate);
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

    private void Claim(IPeerConnection candidate)
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

        PeerConnected?.Invoke(this, new PeerConnectedEventArgs(candidate.RemoteEndPoint));
        _ = ReceiveLoopAsync(candidate);
    }

    private async Task ReceiveLoopAsync(IPeerConnection connection)
    {
        try
        {
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

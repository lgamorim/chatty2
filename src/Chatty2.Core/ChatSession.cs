using System.Net;

namespace Chatty2.Core;

public sealed class ChatSession(IPeerListener listener, IPeerConnector connector) : IChatSession
{
    public const int DefaultPort = 53000;

    private readonly Lock gate = new();
    private IPeerConnection? activeConnection;
    private CancellationTokenSource? listenCts;
    private bool disposed;

    public bool IsConnected
    {
        get
        {
            lock (gate)
            {
                return activeConnection is not null;
            }
        }
    }

    public event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    public event EventHandler<PeerConnectedEventArgs>? PeerConnected;

    public event EventHandler? Disconnected;

    public async Task ListenAsync(int port, CancellationToken cancellationToken)
    {
        listenCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = listenCts.Token;

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

    public async Task ConnectAsync(IPAddress ipAddress, int port, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        listenCts?.Cancel();

        var candidate = await connector.ConnectAsync(ipAddress, port, cancellationToken);
        Claim(candidate);
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        IPeerConnection connection;
        lock (gate)
        {
            connection = activeConnection ?? throw new InvalidOperationException("Not connected to a peer.");
        }

        return connection.SendAsync(message, cancellationToken);
    }

    public void Disconnect()
    {
        IPeerConnection connection;
        lock (gate)
        {
            connection = activeConnection ?? throw new InvalidOperationException("Not connected to a peer.");
        }

        connection.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        listenCts?.Cancel();
        listenCts?.Dispose();

        lock (gate)
        {
            activeConnection?.Dispose();
            activeConnection = null;
        }
    }

    private void Claim(IPeerConnection candidate)
    {
        lock (gate)
        {
            if (activeConnection is not null)
            {
                candidate.Dispose();
                throw new InvalidOperationException("Already connected to a peer.");
            }

            activeConnection = candidate;
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
            lock (gate)
            {
                if (ReferenceEquals(activeConnection, connection)) activeConnection = null;
            }

            connection.Dispose();
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}

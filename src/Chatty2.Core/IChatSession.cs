using System.Net;

namespace Chatty2.Core;

public interface IChatSession : IDisposable
{
    bool IsConnected { get; }

    /// <summary>
    /// The target of the most recent outbound dial that completed its handshake, or
    /// <c>null</c> if none has. Inbound peers are never recorded: their remote port is
    /// ephemeral, so it can't be dialed back.
    /// </summary>
    IPEndPoint? LastDialedEndpoint { get; }

    event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    event EventHandler<PeerConnectedEventArgs>? PeerConnected;

    event EventHandler<PeerIdentifiedEventArgs>? PeerIdentified;

    /// <summary>
    /// The peer sent a line longer than <see cref="ChatSession.MaxMessageLength"/>. The line was
    /// discarded and the session carries on.
    /// </summary>
    event EventHandler<MessageDroppedEventArgs>? MessageDropped;

    event EventHandler? Disconnected;

    event EventHandler<ListenFailedEventArgs>? ListenFailed;

    Task ListenAsync(int port, CancellationToken cancellationToken);

    Task ConnectAsync(IPAddress ipAddress, int port, CancellationToken cancellationToken);

    Task SendAsync(string message, CancellationToken cancellationToken);

    void Disconnect();
}

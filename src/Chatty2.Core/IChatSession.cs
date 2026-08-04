using System.Net;

namespace Chatty2.Core;

public interface IChatSession : IDisposable
{
    bool IsConnected { get; }

    event EventHandler<ChatMessageReceivedEventArgs>? MessageReceived;

    event EventHandler<PeerConnectedEventArgs>? PeerConnected;

    event EventHandler<PeerIdentifiedEventArgs>? PeerIdentified;

    event EventHandler? Disconnected;

    event EventHandler<ListenFailedEventArgs>? ListenFailed;

    Task ListenAsync(int port, CancellationToken cancellationToken);

    Task ConnectAsync(IPAddress ipAddress, int port, CancellationToken cancellationToken);

    Task SendAsync(string message, CancellationToken cancellationToken);

    void Disconnect();
}

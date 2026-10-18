namespace Chatty2.Core;

/// <param name="reason">Why the session ended.</param>
/// <param name="peerName">The name the peer introduced itself with, or <c>null</c> if it never did.</param>
public sealed class DisconnectedEventArgs(DisconnectReason reason, string? peerName) : EventArgs
{
    public DisconnectReason Reason { get; } = reason;

    public string? PeerName { get; } = peerName;
}

namespace Chatty2.Core;

public sealed class PeerConnectedEventArgs(string remoteEndPoint) : EventArgs
{
    public string RemoteEndPoint { get; } = remoteEndPoint;
}

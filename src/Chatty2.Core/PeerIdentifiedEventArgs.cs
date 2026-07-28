namespace Chatty2.Core;

public sealed class PeerIdentifiedEventArgs(string userName) : EventArgs
{
    public string UserName { get; } = userName;
}

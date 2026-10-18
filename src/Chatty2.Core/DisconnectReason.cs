namespace Chatty2.Core;

public enum DisconnectReason
{
    /// <summary>This side ended the session (<c>Disconnect</c>, or the session was disposed).</summary>
    LocalRequest,

    /// <summary>The peer ended the stream cleanly.</summary>
    PeerClosed,

    /// <summary>Reading from the peer failed, for example because the connection was reset.</summary>
    ConnectionLost
}

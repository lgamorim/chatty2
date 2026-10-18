namespace Chatty2.Core;

/// <summary>The peer a session is currently connected to.</summary>
/// <param name="RemoteEndPoint">Where the connection is from or to, as reported by the transport.</param>
/// <param name="Name">The name the peer introduced itself with, or <c>null</c> until it does.</param>
public sealed record PeerInfo(string RemoteEndPoint, string? Name);

using System.Net;
using System.Net.Sockets;
using Chatty2.Core;

namespace Chatty2.App;

internal static class PeerDialer
{
    public static async Task<CommandResult> DialAsync(
        IChatSession session,
        int listeningPort,
        IPAddress ipAddress,
        int port,
        CancellationToken cancellationToken)
    {
        try
        {
            await session.ConnectAsync(ipAddress, port, cancellationToken);
            return CommandResult.Continue();
        }
        catch (InvalidOperationException)
        {
            return CommandResult.Error("Already connected to a peer.");
        }
        catch (SocketException)
        {
            _ = session.ListenAsync(listeningPort, cancellationToken);
            return CommandResult.Error($"Could not connect to {ipAddress}:{port}.");
        }
        catch (IOException)
        {
            // ChatSession.ConnectAsync wraps a failed handshake send (peer connected and
            // dropped immediately) as IOException rather than SocketException. Same
            // recovery as a failed dial: ConnectAsync already cancelled listening before
            // dialing out, so without re-arming it here the app is left neither connected
            // nor listening.
            _ = session.ListenAsync(listeningPort, cancellationToken);
            return CommandResult.Error($"Could not connect to {ipAddress}:{port}.");
        }
    }
}

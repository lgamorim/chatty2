using System.Net;
using System.Net.Sockets;
using Chatty2.Core;

namespace Chatty2.App;

public sealed class ConnectCommand(IChatSession session, int listeningPort) : ICommand
{
    public string Name => "connect";

    public async Task<CommandResult> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments.Length != 2)
            return CommandResult.Error("Usage: /connect <ip-address> <port>");

        if (!IPAddress.TryParse(arguments[0], out var ipAddress))
            return CommandResult.Error($"'{arguments[0]}' is not a valid IP address.");

        if (!int.TryParse(arguments[1], out var port) || port is < 1 or > 65535)
            return CommandResult.Error($"'{arguments[1]}' is not a valid port. Use a number between 1 and 65535.");

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

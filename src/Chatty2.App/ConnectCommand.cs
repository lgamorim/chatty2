using System.Net;
using Chatty2.Core;

namespace Chatty2.App;

public sealed class ConnectCommand(IChatSession session, int listeningPort) : ICommand
{
    public string Name => "connect";

    public Task<CommandResult> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments.Length != 2)
            return Task.FromResult(CommandResult.Error("Usage: /connect <ip-address> <port>"));

        if (!IPAddress.TryParse(arguments[0], out var ipAddress))
            return Task.FromResult(CommandResult.Error($"'{arguments[0]}' is not a valid IP address."));

        if (!int.TryParse(arguments[1], out var port) || port is < 1 or > 65535)
            return Task.FromResult(CommandResult.Error($"'{arguments[1]}' is not a valid port. Use a number between 1 and 65535."));

        return PeerDialer.DialAsync(session, listeningPort, ipAddress, port, cancellationToken);
    }
}

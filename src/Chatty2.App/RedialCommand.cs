using Chatty2.Core;

namespace Chatty2.App;

public sealed class RedialCommand(IChatSession session, int listeningPort) : ICommand
{
    public string Name => "redial";

    public Task<CommandResult> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments.Length != 0)
            return Task.FromResult(CommandResult.Error("Usage: /redial"));

        var endpoint = session.LastDialedEndpoint;
        if (endpoint is null)
            return Task.FromResult(CommandResult.Error("No previous /connect target to redial. Use /connect <ip-address> <port>."));

        return PeerDialer.DialAsync(session, listeningPort, endpoint.Address, endpoint.Port, cancellationToken);
    }
}

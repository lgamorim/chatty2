using Chatty2.Core;

namespace Chatty2.App;

public sealed class DisconnectCommand(IChatSession session) : ICommand
{
    public string Name => "disconnect";

    public Task<CommandResult> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            session.Disconnect();
            return Task.FromResult(CommandResult.Continue());
        }
        catch (InvalidOperationException)
        {
            return Task.FromResult(CommandResult.Error("Not connected to a peer."));
        }
    }
}

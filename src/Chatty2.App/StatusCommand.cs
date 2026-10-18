using System.Globalization;
using Chatty2.Core;

namespace Chatty2.App;

public sealed class StatusCommand(IChatSession session, string localName, int listeningPort) : ICommand
{
    public string Name => "status";

    public Task<CommandResult> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        if (arguments.Length != 0)
            return Task.FromResult(CommandResult.Error("Usage: /status"));

        var lines = new[]
        {
            Line("Name:", localName),
            Line("Listening port:", listeningPort.ToString(CultureInfo.InvariantCulture)),
            Line("Connection:", Describe(session.Peer))
        };

        return Task.FromResult(CommandResult.Continue(string.Join(Environment.NewLine, lines)));
    }

    private static string Line(string label, string value) => $"{label,-16}{value}";

    private static string Describe(PeerInfo? peer) => peer switch
    {
        null => "not connected",
        { Name: { } name } => $"connected to {name} ({peer.RemoteEndPoint})",
        _ => $"connected to {peer.RemoteEndPoint}"
    };
}

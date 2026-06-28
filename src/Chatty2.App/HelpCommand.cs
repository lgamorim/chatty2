namespace Chatty2.App;

public sealed class HelpCommand : ICommand
{
    public string Name => "help";

    public Task<CommandResult> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var helpText = """
            Type a message and press Enter to send it to the connected peer.
            Available commands:
              /connect <ip-address> <port>  Connect to a peer at the given IP address and port.
              /disconnect                   End the current chat session without closing the app.
              /help                         Show this help message.
              /exit                         Close the application.
            """;

        return Task.FromResult(CommandResult.Continue(helpText));
    }
}

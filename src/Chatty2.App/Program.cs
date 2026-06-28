using Chatty2.App;
using Chatty2.Core;

var listeningPort = ChatSession.DefaultPort;
if (args.Length > 0)
{
    if (args.Length != 2
        || args[0] is not ("--port" or "-p")
        || !int.TryParse(args[1], out listeningPort)
        || listeningPort is < 1 or > 65535)
    {
        Console.Error.WriteLine("Usage: Chatty2.App [--port <port>]");
        return 1;
    }
}

using var session = new ChatSession(new TcpPeerListener(), new TcpPeerConnector());

ICommand[] commands =
[
    new ConnectCommand(session, listeningPort),
    new DisconnectCommand(session),
    new HelpCommand(),
    new ExitCommand()
];

var runner = new ConsoleAppRunner(commands, session, listeningPort, Console.In, Console.Out, Console.Error);

return await runner.RunAsync(CancellationToken.None);

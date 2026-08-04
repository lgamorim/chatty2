using Chatty2.App;
using Chatty2.Core;

var listeningPort = ChatSession.DefaultPort;
var userName = Environment.UserName;

for (var i = 0; i < args.Length; i += 2)
{
    if (i + 1 >= args.Length) return PrintUsageAndExit();

    switch (args[i])
    {
        case "--port" or "-p":
            if (!int.TryParse(args[i + 1], out listeningPort) || listeningPort is < 1 or > 65535)
                return PrintUsageAndExit();
            break;
        case "--name" or "-n":
            if (string.IsNullOrWhiteSpace(args[i + 1])) return PrintUsageAndExit();
            userName = args[i + 1];
            break;
        default:
            return PrintUsageAndExit();
    }
}

ChatSession session;
try
{
    session = new ChatSession(new TcpPeerListener(), new TcpPeerConnector(), userName);
}
catch (ArgumentException)
{
    // Covers an empty Environment.UserName default (legitimate on some service accounts
    // and container images) the same way every other bad input here is handled - a usage
    // message instead of an unhandled exception escaping into Main.
    return PrintUsageAndExit();
}

using (session)
{
    ICommand[] commands =
    [
        new ConnectCommand(session, listeningPort),
        new DisconnectCommand(session),
        new HelpCommand(),
        new ExitCommand()
    ];

    var runner = new ConsoleAppRunner(commands, session, listeningPort, Console.In, Console.Out, Console.Error);

    return await runner.RunAsync(CancellationToken.None);
}

static int PrintUsageAndExit()
{
    Console.Error.WriteLine("Usage: Chatty2.App [--port <port>] [--name <name>]");
    return 1;
}

using Chatty2.App;
using Chatty2.Core;

var listeningPort = ChatSession.DefaultPort;
var userName = Environment.UserName;
string? logPath = null;

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
        case "--log":
            if (string.IsNullOrWhiteSpace(args[i + 1])) return PrintUsageAndExit();
            logPath = args[i + 1];
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
catch (ArgumentException exception)
{
    // Covers an empty Environment.UserName default (legitimate on some service accounts
    // and container images) the same way every other bad input here is handled - a usage
    // message instead of an unhandled exception escaping into Main. The validation message
    // itself says which constraint was violated, which the generic usage line does not.
    Console.Error.WriteLine(exception.Message);
    return PrintUsageAndExit();
}

using (session)
{
    ChatLog? chatLog = null;
    if (logPath is not null)
    {
        try
        {
            // Opened for append (created if missing) and readable by others while it is open,
            // so the file can be followed with tail -f. A missing directory is an error here
            // rather than something to create behind the user's back.
            chatLog = new ChatLog(File.AppendText(logPath), userName, TimeProvider.System);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine($"Could not open the log file '{logPath}': {exception.Message}");
            return PrintUsageAndExit();
        }
    }

    using var log = chatLog;

    ICommand[] commands =
    [
        new ConnectCommand(session, listeningPort),
        new RedialCommand(session, listeningPort),
        new DisconnectCommand(session),
        new StatusCommand(session, userName, listeningPort),
        new HelpCommand(),
        new ExitCommand()
    ];

    var runner = new ConsoleAppRunner(
        commands, session, listeningPort, Console.In, Console.Out, Console.Error, chatLog: chatLog);

    return await runner.RunAsync(CancellationToken.None);
}

static int PrintUsageAndExit()
{
    Console.Error.WriteLine("Usage: Chatty2.App [--port <port>] [--name <name>] [--log <path>]");
    return 1;
}

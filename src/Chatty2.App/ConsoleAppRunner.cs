using Chatty2.Core;

namespace Chatty2.App;

public sealed class ConsoleAppRunner
{
    private readonly Dictionary<string, ICommand> commands;
    private readonly IChatSession session;
    private readonly int listeningPort;
    private readonly TextReader input;
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly Lock outputLock = new();
    private CancellationToken cancellationToken;

    public ConsoleAppRunner(
        IEnumerable<ICommand> commands,
        IChatSession session,
        int listeningPort,
        TextReader input,
        TextWriter output,
        TextWriter error)
    {
        this.commands = commands.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
        this.session = session;
        this.listeningPort = listeningPort;
        this.input = input;
        this.output = output;
        this.error = error;

        session.MessageReceived += OnMessageReceived;
        session.PeerConnected += OnPeerConnected;
        session.Disconnected += OnDisconnected;
        session.ListenFailed += OnListenFailed;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        this.cancellationToken = cancellationToken;

        try
        {
            return await RunLoopAsync();
        }
        catch (Exception exception)
        {
            WriteLine(error, exception.Message, ConsoleColor.Red, () => Console.IsErrorRedirected);
            return 1;
        }
        finally
        {
            // Unsubscribe before disposing: disposing the session ends any active
            // connection, which would otherwise raise Disconnected one more time and
            // print a spurious notice (and trigger a pointless re-listen) after exit.
            session.MessageReceived -= OnMessageReceived;
            session.PeerConnected -= OnPeerConnected;
            session.Disconnected -= OnDisconnected;
            session.ListenFailed -= OnListenFailed;
            session.Dispose();
        }
    }

    private async Task<int> RunLoopAsync()
    {
        _ = session.ListenAsync(listeningPort, cancellationToken);

        while (true)
        {
            var line = input.ReadLine();
            if (line is null) return 0;
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith('/'))
            {
                if (await DispatchCommandAsync(line)) return 0;
            }
            else
            {
                await SendMessageAsync(line);
            }
        }
    }

    private async Task<bool> DispatchCommandAsync(string line)
    {
        var parts = line[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            WriteError("Please specify a command. Type /help for a list of commands.");
            return false;
        }

        if (!commands.TryGetValue(parts[0], out var command))
        {
            WriteError($"Unknown command '/{parts[0]}'. Type /help for a list of commands.");
            return false;
        }

        var result = await command.ExecuteAsync(parts[1..], cancellationToken);
        if (result.Message is not null)
        {
            if (result.IsError) WriteError(result.Message);
            else WriteInfo(result.Message);
        }

        return result.ShouldExit;
    }

    private async Task SendMessageAsync(string message)
    {
        if (!session.IsConnected)
        {
            WriteError("Not connected to a peer. Use /connect <ip-address> <port> to start a conversation.");
            return;
        }

        try
        {
            await session.SendAsync(message, cancellationToken);
        }
        catch (Exception)
        {
            WriteError("Message could not be sent. The peer may have disconnected.");
        }
    }

    private void OnMessageReceived(object? sender, ChatMessageReceivedEventArgs e) => WriteInfo($"[peer] {e.Message}");

    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e) => WriteInfo($"Connected to {e.RemoteEndPoint}.");

    private void OnDisconnected(object? sender, EventArgs e)
    {
        WriteInfo("Peer disconnected.");
        _ = session.ListenAsync(listeningPort, cancellationToken);
    }

    private void OnListenFailed(object? sender, ListenFailedEventArgs e) =>
        WriteError($"Stopped listening for incoming connections: {e.Exception.Message}");

    private void WriteInfo(string message) => WriteLine(output, message, ConsoleColor.Yellow, () => Console.IsOutputRedirected);

    private void WriteError(string message) => WriteLine(output, message, ConsoleColor.Red, () => Console.IsOutputRedirected);

    private void WriteLine(TextWriter writer, string message, ConsoleColor color, Func<bool> isRedirected)
    {
        lock (outputLock)
        {
            if (!isRedirected()) Console.ForegroundColor = color;

            writer.WriteLine(message);

            if (!isRedirected()) Console.ForegroundColor = ConsoleColor.White;
        }
    }
}

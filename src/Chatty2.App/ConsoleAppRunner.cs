using Chatty2.Core;

namespace Chatty2.App;

public sealed class ConsoleAppRunner
{
    private const string Prompt = "C2> ";

    private readonly Dictionary<string, ICommand> _commands;
    private readonly IChatSession _session;
    private readonly int _listeningPort;
    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly Func<bool> _isInputRedirected;
    private readonly Lock _outputLock = new();
    private CancellationToken _cancellationToken;
    private bool _promptPending;
    private string _peerName = "peer";

    public ConsoleAppRunner(
        IEnumerable<ICommand> commands,
        IChatSession session,
        int listeningPort,
        TextReader input,
        TextWriter output,
        TextWriter error,
        Func<bool>? isInputRedirected = null)
    {
        _commands = commands.ToDictionary(command => command.Name, StringComparer.OrdinalIgnoreCase);
        _session = session;
        _listeningPort = listeningPort;
        _input = input;
        _output = output;
        _error = error;
        _isInputRedirected = isInputRedirected ?? (() => Console.IsInputRedirected);

        _session.MessageReceived += OnMessageReceived;
        _session.PeerConnected += OnPeerConnected;
        _session.PeerIdentified += OnPeerIdentified;
        _session.Disconnected += OnDisconnected;
        _session.ListenFailed += OnListenFailed;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;

        try
        {
            return await RunLoopAsync();
        }
        catch (Exception exception)
        {
            WriteLine(_error, exception.Message, ConsoleColor.Red, () => Console.IsErrorRedirected);
            return 1;
        }
        finally
        {
            // Unsubscribe before disposing: disposing the session ends any active
            // connection, which would otherwise raise Disconnected one more time and
            // print a spurious notice (and trigger a pointless re-listen) after exit.
            _session.MessageReceived -= OnMessageReceived;
            _session.PeerConnected -= OnPeerConnected;
            _session.PeerIdentified -= OnPeerIdentified;
            _session.Disconnected -= OnDisconnected;
            _session.ListenFailed -= OnListenFailed;
            _session.Dispose();
        }
    }

    private async Task<int> RunLoopAsync()
    {
        _ = _session.ListenAsync(_listeningPort, _cancellationToken);

        while (true)
        {
            WritePrompt();

            var line = _input.ReadLine();
            lock (_outputLock)
            {
                // Reaching end-of-stream right after a prompt was written leaves it sitting on
                // a bare line with no trailing newline; finish that line before exiting so the
                // shell's own prompt doesn't land glued to it. Only needed when a prompt was
                // actually shown — WritePrompt is a no-op while input is redirected.
                if (line is null && _promptPending) _output.WriteLine();
                _promptPending = false;
            }

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

        if (!_commands.TryGetValue(parts[0], out var command))
        {
            WriteError($"Unknown command '/{parts[0]}'. Type /help for a list of commands.");
            return false;
        }

        var result = await command.ExecuteAsync(parts[1..], _cancellationToken);
        if (result.Message is not null)
        {
            if (result.IsError) WriteError(result.Message);
            else WriteInfo(result.Message);
        }

        return result.ShouldExit;
    }

    private async Task SendMessageAsync(string message)
    {
        if (!_session.IsConnected)
        {
            WriteError("Not connected to a peer. Use /connect <ip-address> <port> to start a conversation.");
            return;
        }

        try
        {
            await _session.SendAsync(message, _cancellationToken);
        }
        catch (Exception)
        {
            WriteError("Message could not be sent. The peer may have disconnected.");
        }
    }

    private void OnMessageReceived(object? sender, ChatMessageReceivedEventArgs e)
    {
        string peerName;
        lock (_outputLock)
        {
            peerName = _peerName;
        }

        WriteInfo($"[{peerName}] {e.Message}");
    }

    private void OnPeerConnected(object? sender, PeerConnectedEventArgs e) => WriteInfo($"Connected to {e.RemoteEndPoint}.");

    private void OnPeerIdentified(object? sender, PeerIdentifiedEventArgs e)
    {
        // OnMessageReceived and OnDisconnected can run on a different receive-loop thread
        // than this one raised it (a re-listen hands the next connection to a fresh task),
        // so this needs the same lock discipline as the rest of this class's shared state
        // rather than a second ad hoc synchronization scheme.
        lock (_outputLock)
        {
            _peerName = e.UserName;
        }
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        WriteInfo("Peer disconnected.");
        lock (_outputLock)
        {
            _peerName = "peer";
        }

        _ = _session.ListenAsync(_listeningPort, _cancellationToken);
    }

    private void OnListenFailed(object? sender, ListenFailedEventArgs e) =>
        WriteError($"Stopped listening for incoming connections: {e.Exception.Message}");

    private void WritePrompt()
    {
        // No one is watching a redirected/piped stdin for a prompt, and writing it there would
        // just interleave noise into whatever's consuming the output.
        if (_isInputRedirected()) return;

        lock (_outputLock)
        {
            _output.Write(Prompt);
            _promptPending = true;
        }
    }

    private void WriteInfo(string message) => WriteLine(_output, message, ConsoleColor.Yellow, () => Console.IsOutputRedirected);

    private void WriteError(string message) => WriteLine(_output, message, ConsoleColor.Red, () => Console.IsOutputRedirected);

    private void WriteLine(TextWriter writer, string message, ConsoleColor color, Func<bool> isRedirected)
    {
        lock (_outputLock)
        {
            // A pending prompt (written but not yet followed by a completed ReadLine) sits on
            // a bare line with no trailing newline. Writing straight over it would land this
            // message on the same line as the prompt, so move to a fresh line first and redraw
            // the prompt afterward — this doesn't restore any input the user had already typed,
            // but it does leave them looking at a usable prompt again instead of a dead line.
            var redrawPrompt = _promptPending && ReferenceEquals(writer, _output);
            if (redrawPrompt) writer.WriteLine();

            if (!isRedirected()) Console.ForegroundColor = color;

            try
            {
                writer.WriteLine(message);
            }
            finally
            {
                if (!isRedirected()) Console.ForegroundColor = ConsoleColor.White;
            }

            if (redrawPrompt) writer.Write(Prompt);
        }
    }
}

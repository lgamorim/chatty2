using System.Net.Sockets;
using System.Text;
using Chatty2.Core;
using NSubstitute;
using Xunit;

namespace Chatty2.App.UnitTests;

public class ConsoleAppRunnerTests
{
    [Fact]
    public async Task Should_ReturnZero_When_InputReachesEndOfStreamWithoutExitCommand()
    {
        var session = Substitute.For<IChatSession>();
        var runner = new ConsoleAppRunner(
            [new HelpCommand(), new ExitCommand()], session, ChatSession.DefaultPort,
            new StringReader(string.Empty), new StringWriter(), new StringWriter());

        var exitCode = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task Should_ReturnZeroAndWriteGoodbye_When_ExitCommandEntered()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var runner = new ConsoleAppRunner(
            [new ExitCommand()], session, ChatSession.DefaultPort,
            new StringReader("/exit\n"), output, new StringWriter());

        var exitCode = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Contains("Goodbye", output.ToString());
    }

    [Fact]
    public async Task Should_WritePrompt_BeforeReadingInput()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var runner = new ConsoleAppRunner(
            [new ExitCommand()], session, ChatSession.DefaultPort,
            new StringReader("/exit\n"), output, new StringWriter(),
            isInputRedirected: () => false);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("C2> ", output.ToString());
    }

    [Fact]
    public async Task Should_WritePrompt_OncePerLoopIteration()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var runner = new ConsoleAppRunner(
            [new HelpCommand(), new ExitCommand()], session, ChatSession.DefaultPort,
            new StringReader("/help\n/exit\n"), output, new StringWriter(),
            isInputRedirected: () => false);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        var promptCount = output.ToString().Split("C2> ").Length - 1;
        Assert.Equal(2, promptCount);
    }

    [Fact]
    public async Task Should_RedrawPromptOnFreshLine_When_MessageReceivedWhilePromptIsPending()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var input = new EventRaisingReader(new StringReader("/exit\n"), () =>
            session.MessageReceived += Raise.EventWith(new ChatMessageReceivedEventArgs("hi there")));
        var runner = new ConsoleAppRunner(
            [new ExitCommand()], session, ChatSession.DefaultPort, input, output, new StringWriter(),
            isInputRedirected: () => false);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal($"C2> {Environment.NewLine}[peer] hi there{Environment.NewLine}C2> Goodbye!{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task Should_NotRedrawPrompt_When_MessageReceivedAfterPromptAlreadyConsumed()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var runner = new ConsoleAppRunner(
            [new ExitCommand()], session, ChatSession.DefaultPort, new StringReader("/exit\n"), output, new StringWriter(),
            isInputRedirected: () => false);

        session.MessageReceived += Raise.EventWith(new ChatMessageReceivedEventArgs("hi there"));

        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal($"[peer] hi there{Environment.NewLine}C2> Goodbye!{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task Should_NotWritePrompt_When_InputIsRedirected()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var runner = new ConsoleAppRunner(
            [new ExitCommand()], session, ChatSession.DefaultPort,
            new StringReader("/exit\n"), output, new StringWriter(),
            isInputRedirected: () => true);

        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal($"Goodbye!{Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task Should_WriteTrailingNewline_When_InputReachesEndOfStreamWhilePromptPending()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var runner = new ConsoleAppRunner(
            [new ExitCommand()], session, ChatSession.DefaultPort,
            new StringReader(string.Empty), output, new StringWriter(),
            isInputRedirected: () => false);

        var exitCode = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal($"C2> {Environment.NewLine}", output.ToString());
    }

    [Fact]
    public async Task Should_NotWriteTrailingNewline_When_InputRedirectedReachesEndOfStream()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var runner = new ConsoleAppRunner(
            [new ExitCommand()], session, ChatSession.DefaultPort,
            new StringReader(string.Empty), output, new StringWriter(),
            isInputRedirected: () => true);

        var exitCode = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
    }

    private sealed class EventRaisingReader(TextReader inner, Action onFirstReadLine) : TextReader
    {
        private bool _raised;

        public override string? ReadLine()
        {
            if (!_raised)
            {
                _raised = true;
                onFirstReadLine();
            }

            return inner.ReadLine();
        }
    }

    [Fact]
    public async Task Should_SkipBlankLines_WithoutDispatching()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var input = new StringReader("\n   \n/exit\n");
        var runner = new ConsoleAppRunner([new ExitCommand()], session, ChatSession.DefaultPort, input, output, new StringWriter());

        var exitCode = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Unknown command", output.ToString());
    }

    [Fact]
    public async Task Should_WriteFriendlyError_When_UnknownCommandEntered()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var input = new StringReader("/foo\n/exit\n");
        var runner = new ConsoleAppRunner([new ExitCommand()], session, ChatSession.DefaultPort, input, output, new StringWriter());

        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Unknown command '/foo'. Type /help for a list of commands.", output.ToString());
    }

    [Fact]
    public async Task Should_WriteFriendlyError_When_SlashEnteredWithoutCommandName()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var input = new StringReader("/\n/exit\n");
        var runner = new ConsoleAppRunner([new ExitCommand()], session, ChatSession.DefaultPort, input, output, new StringWriter());

        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Please specify a command. Type /help for a list of commands.", output.ToString());
    }

    [Fact]
    public async Task Should_MatchCommandNameCaseInsensitively()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var input = new StringReader("/HELP\n/exit\n");
        var runner = new ConsoleAppRunner([new HelpCommand(), new ExitCommand()], session, ChatSession.DefaultPort, input, output, new StringWriter());

        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Contains("/connect", output.ToString());
    }

    [Fact]
    public async Task Should_SendMessage_When_ConnectedAndLineHasNoSlash()
    {
        var session = Substitute.For<IChatSession>();
        session.IsConnected.Returns(true);
        var input = new StringReader("hello there\n/exit\n");
        var runner = new ConsoleAppRunner([new ExitCommand()], session, ChatSession.DefaultPort, input, new StringWriter(), new StringWriter());

        await runner.RunAsync(TestContext.Current.CancellationToken);

        await session.Received(1).SendAsync("hello there", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_WriteFriendlyWarningWithoutSending_When_NotConnectedAndLineHasNoSlash()
    {
        var session = Substitute.For<IChatSession>();
        session.IsConnected.Returns(false);
        var output = new StringWriter();
        var input = new StringReader("hello there\n/exit\n");
        var runner = new ConsoleAppRunner([new ExitCommand()], session, ChatSession.DefaultPort, input, output, new StringWriter());

        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Not connected", output.ToString());
        await session.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_WriteEventNotifications_When_EventsRaisedBeforeRunAsync()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var runner = new ConsoleAppRunner([new ExitCommand()], session, ChatSession.DefaultPort, new StringReader("/exit\n"), output, new StringWriter());

        session.MessageReceived += Raise.EventWith(new ChatMessageReceivedEventArgs("hi there"));
        session.PeerConnected += Raise.EventWith(new PeerConnectedEventArgs("10.0.0.2:53000"));
        session.Disconnected += Raise.Event<EventHandler>();

        await runner.RunAsync(TestContext.Current.CancellationToken);

        var text = output.ToString();
        Assert.Contains("hi there", text);
        Assert.Contains("10.0.0.2:53000", text);
        Assert.Contains("disconnected", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_WriteErrorMessage_When_ListenFailedEventRaised()
    {
        var session = Substitute.For<IChatSession>();
        var output = new StringWriter();
        var runner = new ConsoleAppRunner([new ExitCommand()], session, ChatSession.DefaultPort, new StringReader("/exit\n"), output, new StringWriter());

        session.ListenFailed += Raise.EventWith(new ListenFailedEventArgs(new SocketException()));

        await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Stopped listening", output.ToString());
    }

    [Fact]
    public async Task Should_ContinueLoop_When_SendAsyncThrowsMidLoop()
    {
        var session = Substitute.For<IChatSession>();
        session.IsConnected.Returns(true);
        session.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(new IOException("broken")));
        var output = new StringWriter();
        var input = new StringReader("hello\n/exit\n");
        var runner = new ConsoleAppRunner([new ExitCommand()], session, ChatSession.DefaultPort, input, output, new StringWriter());

        var exitCode = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        Assert.Contains("could not be sent", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Should_ReturnOneAndWriteErrorMessage_When_CommandThrowsUnhandledException()
    {
        var session = Substitute.For<IChatSession>();
        var throwingCommand = Substitute.For<ICommand>();
        throwingCommand.Name.Returns("boom");
        throwingCommand.ExecuteAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CommandResult>(new InvalidOperationException("kaboom")));

        var error = new StringWriter();
        var input = new StringReader("/boom\n");
        var runner = new ConsoleAppRunner([throwingCommand], session, ChatSession.DefaultPort, input, new StringWriter(), error);

        var exitCode = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains("kaboom", error.ToString());
    }

    [Fact]
    public async Task Should_DisposeSession_When_RunCompletesNormally()
    {
        var session = Substitute.For<IChatSession>();
        var runner = new ConsoleAppRunner([new ExitCommand()], session, ChatSession.DefaultPort, new StringReader("/exit\n"), new StringWriter(), new StringWriter());

        await runner.RunAsync(TestContext.Current.CancellationToken);

        session.Received(1).Dispose();
    }

    [Fact]
    public async Task Should_DisposeSession_When_UnhandledExceptionThrown()
    {
        var session = Substitute.For<IChatSession>();
        var throwingCommand = Substitute.For<ICommand>();
        throwingCommand.Name.Returns("boom");
        throwingCommand.ExecuteAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<CommandResult>(new InvalidOperationException("kaboom")));
        var runner = new ConsoleAppRunner([throwingCommand], session, ChatSession.DefaultPort, new StringReader("/boom\n"), new StringWriter(), new StringWriter());

        await runner.RunAsync(TestContext.Current.CancellationToken);

        session.Received(1).Dispose();
    }

    [Fact]
    public async Task Should_StartListening_WithConfiguredPort_NotHardcodedDefault()
    {
        var session = Substitute.For<IChatSession>();
        const int configuredPort = 61234;
        var runner = new ConsoleAppRunner([new ExitCommand()], session, configuredPort, new StringReader("/exit\n"), new StringWriter(), new StringWriter());

        await runner.RunAsync(TestContext.Current.CancellationToken);

        await session.Received(1).ListenAsync(configuredPort, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ProduceExpectedTranscript_When_MultipleLinesAndEventsInterleave()
    {
        var session = Substitute.For<IChatSession>();
        session.IsConnected.Returns(true);
        var output = new StringWriter();
        var input = new StringReader("/help\nhello\n/exit\n");
        var runner = new ConsoleAppRunner([new HelpCommand(), new ExitCommand()], session, ChatSession.DefaultPort, input, output, new StringWriter());

        session.MessageReceived += Raise.EventWith(new ChatMessageReceivedEventArgs("hi from peer"));

        var exitCode = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, exitCode);
        var text = output.ToString();
        Assert.Contains("hi from peer", text);
        Assert.Contains("/connect", text);
        Assert.Contains("Goodbye", text);
        await session.Received(1).SendAsync("hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_PropagateExceptionWithoutSwallowingIt_When_OutputWriterThrowsDuringWrite()
    {
        // The write is wrapped in try/finally (so the color reset still runs even if the
        // write itself throws); this guards against that restructuring accidentally
        // swallowing the original exception instead of letting it propagate.
        var session = Substitute.For<IChatSession>();
        var error = new StringWriter();
        var input = new StringReader("/help\n");
        var runner = new ConsoleAppRunner([new HelpCommand()], session, ChatSession.DefaultPort, input, new ThrowingTextWriter(), error);

        var exitCode = await runner.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, exitCode);
        Assert.Contains("broken pipe", error.ToString());
    }

    private sealed class ThrowingTextWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void WriteLine(string? value) => throw new IOException("broken pipe");
    }
}

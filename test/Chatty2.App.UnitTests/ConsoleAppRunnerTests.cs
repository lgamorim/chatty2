using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

        Assert.Contains("Unknown command", output.ToString());
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
}

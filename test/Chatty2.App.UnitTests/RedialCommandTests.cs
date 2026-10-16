using System.Net;
using System.Net.Sockets;
using Chatty2.Core;
using NSubstitute;
using Xunit;

namespace Chatty2.App.UnitTests;

public class RedialCommandTests
{
    private static readonly IPEndPoint Remembered = new(IPAddress.Parse("192.168.1.5"), 53001);

    [Theory]
    [InlineData(new object[] { new[] { "192.168.1.5" } })]
    [InlineData(new object[] { new[] { "192.168.1.5", "53001" } })]
    public async Task Should_ReturnUsageError_When_ArgumentsAreGiven(string[] arguments)
    {
        var session = Substitute.For<IChatSession>();
        session.LastDialedEndpoint.Returns(Remembered);
        var command = new RedialCommand(session, ChatSession.DefaultPort);

        var result = await command.ExecuteAsync(arguments, TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("Usage: /redial", result.Message);
        await session.DidNotReceive().ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnFriendlyError_When_NothingHasBeenDialed()
    {
        var session = Substitute.For<IChatSession>();
        var command = new RedialCommand(session, ChatSession.DefaultPort);

        var result = await command.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("No previous", result.Message);
        Assert.Contains("/connect", result.Message);
        await session.DidNotReceive().ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ConnectToLastDialedEndpoint_When_OneIsRemembered()
    {
        var session = Substitute.For<IChatSession>();
        session.LastDialedEndpoint.Returns(Remembered);
        var command = new RedialCommand(session, ChatSession.DefaultPort);
        using var cts = new CancellationTokenSource();

        var result = await command.ExecuteAsync([], cts.Token);

        Assert.False(result.ShouldExit);
        Assert.False(result.IsError);
        Assert.Null(result.Message);
        await session.Received(1).ConnectAsync(Remembered.Address, Remembered.Port, cts.Token);
    }

    [Fact]
    public async Task Should_ReturnFriendlyError_When_AlreadyConnected()
    {
        var session = Substitute.For<IChatSession>();
        session.LastDialedEndpoint.Returns(Remembered);
        session.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Already connected to a peer.")));
        var command = new RedialCommand(session, ChatSession.DefaultPort);

        var result = await command.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("Already connected", result.Message);
    }

    [Fact]
    public async Task Should_ReturnFriendlyErrorNamingTheTargetAndRestartListening_When_DialFails()
    {
        var session = Substitute.For<IChatSession>();
        session.LastDialedEndpoint.Returns(Remembered);
        session.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new SocketException()));
        var command = new RedialCommand(session, ChatSession.DefaultPort);
        using var cts = new CancellationTokenSource();

        var result = await command.ExecuteAsync([], cts.Token);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("Could not connect to 192.168.1.5:53001", result.Message);
        await session.Received(1).ListenAsync(ChatSession.DefaultPort, cts.Token);
    }

    [Fact]
    public async Task Should_ReturnFriendlyErrorAndRestartListening_When_HandshakeSendFails()
    {
        var session = Substitute.For<IChatSession>();
        session.LastDialedEndpoint.Returns(Remembered);
        session.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("broken")));
        var command = new RedialCommand(session, ChatSession.DefaultPort);
        using var cts = new CancellationTokenSource();

        var result = await command.ExecuteAsync([], cts.Token);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("Could not connect", result.Message);
        await session.Received(1).ListenAsync(ChatSession.DefaultPort, cts.Token);
    }
}

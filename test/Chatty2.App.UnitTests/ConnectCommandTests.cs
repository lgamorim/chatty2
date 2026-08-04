using System.Net;
using System.Net.Sockets;
using Chatty2.Core;
using NSubstitute;
using Xunit;

namespace Chatty2.App.UnitTests;

public class ConnectCommandTests
{
    [Theory]
    [InlineData(new object[] { new string[0] })]
    [InlineData(new object[] { new[] { "192.168.1.5" } })]
    [InlineData(new object[] { new[] { "192.168.1.5", "53000", "extra" } })]
    public async Task Should_ReturnUsageError_When_ArgumentCountIsWrong(string[] arguments)
    {
        var session = Substitute.For<IChatSession>();
        var command = new ConnectCommand(session, ChatSession.DefaultPort);

        var result = await command.ExecuteAsync(arguments, TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("Usage", result.Message);
        await session.DidNotReceive().ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ReturnFriendlyError_When_IpAddressIsInvalid()
    {
        var session = Substitute.For<IChatSession>();
        var command = new ConnectCommand(session, ChatSession.DefaultPort);

        var result = await command.ExecuteAsync(["not-an-ip", "53000"], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("not a valid IP", result.Message);
        await session.DidNotReceive().ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not-a-port")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    public async Task Should_ReturnFriendlyError_When_PortIsInvalid(string port)
    {
        var session = Substitute.For<IChatSession>();
        var command = new ConnectCommand(session, ChatSession.DefaultPort);

        var result = await command.ExecuteAsync(["192.168.1.5", port], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("not a valid port", result.Message);
        await session.DidNotReceive().ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Should_ConnectWithParsedIpAndPort_When_ArgumentsAreValid()
    {
        var session = Substitute.For<IChatSession>();
        var command = new ConnectCommand(session, ChatSession.DefaultPort);
        using var cts = new CancellationTokenSource();

        var result = await command.ExecuteAsync(["192.168.1.5", "53001"], cts.Token);

        Assert.False(result.ShouldExit);
        Assert.False(result.IsError);
        Assert.Null(result.Message);
        await session.Received(1).ConnectAsync(IPAddress.Parse("192.168.1.5"), 53001, cts.Token);
    }

    [Fact]
    public async Task Should_ReturnFriendlyError_When_AlreadyConnected()
    {
        var session = Substitute.For<IChatSession>();
        session.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Already connected to a peer.")));
        var command = new ConnectCommand(session, ChatSession.DefaultPort);

        var result = await command.ExecuteAsync(["192.168.1.5", "53001"], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("Already connected", result.Message);
    }

    [Fact]
    public async Task Should_ReturnFriendlyErrorAndRestartListening_When_DialFails()
    {
        var session = Substitute.For<IChatSession>();
        session.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new SocketException()));
        var command = new ConnectCommand(session, ChatSession.DefaultPort);
        using var cts = new CancellationTokenSource();

        var result = await command.ExecuteAsync(["192.168.1.5", "53001"], cts.Token);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("Could not connect", result.Message);
        await session.Received(1).ListenAsync(ChatSession.DefaultPort, cts.Token);
    }

    [Fact]
    public async Task Should_ReturnFriendlyErrorAndRestartListening_When_HandshakeSendFails()
    {
        // ChatSession.ConnectAsync wraps a failed handshake send as IOException (the dial
        // step itself only ever throws SocketException) - this must be handled the same way
        // as a failed dial, not left to escape into ConsoleAppRunner's outer catch and kill
        // the app over what a real user would see as just a failed /connect attempt.
        var session = Substitute.For<IChatSession>();
        session.ConnectAsync(Arg.Any<IPAddress>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("broken")));
        var command = new ConnectCommand(session, ChatSession.DefaultPort);
        using var cts = new CancellationTokenSource();

        var result = await command.ExecuteAsync(["192.168.1.5", "53001"], cts.Token);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("Could not connect", result.Message);
        await session.Received(1).ListenAsync(ChatSession.DefaultPort, cts.Token);
    }
}

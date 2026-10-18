using Chatty2.Core;
using NSubstitute;
using Xunit;

namespace Chatty2.App.UnitTests;

public class StatusCommandTests
{
    [Fact]
    public async Task Should_ReportNameAndPortAndNoConnection_When_NotConnected()
    {
        var session = Substitute.For<IChatSession>();
        var command = new StatusCommand(session, "Alice", 53001);

        var result = await command.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.False(result.IsError);
        Assert.NotNull(result.Message);
        Assert.Equal(
            [
                "Name:           Alice",
                "Listening port: 53001",
                "Connection:     not connected"
            ],
            result.Message.Split(Environment.NewLine));
    }

    [Fact]
    public async Task Should_ReportPeerNameAndEndpoint_When_ConnectedToAnIdentifiedPeer()
    {
        var session = Substitute.For<IChatSession>();
        session.Peer.Returns(new PeerInfo("127.0.0.1:60335", "Bob"));
        var command = new StatusCommand(session, "Alice", ChatSession.DefaultPort);

        var result = await command.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.NotNull(result.Message);
        Assert.Equal(
            [
                "Name:           Alice",
                "Listening port: 53000",
                "Connection:     connected to Bob (127.0.0.1:60335)"
            ],
            result.Message.Split(Environment.NewLine));
    }

    [Fact]
    public async Task Should_ReportEndpointOnly_When_ConnectedButThePeerHasNotIdentifiedYet()
    {
        var session = Substitute.For<IChatSession>();
        session.Peer.Returns(new PeerInfo("127.0.0.1:60335", null));
        var command = new StatusCommand(session, "Alice", ChatSession.DefaultPort);

        var result = await command.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("Connection:     connected to 127.0.0.1:60335", result.Message);
        Assert.DoesNotContain("(", result.Message);
    }

    [Theory]
    [InlineData(new object[] { new[] { "now" } })]
    [InlineData(new object[] { new[] { "now", "please" } })]
    public async Task Should_ReturnUsageError_When_ArgumentsAreGiven(string[] arguments)
    {
        var session = Substitute.For<IChatSession>();
        var command = new StatusCommand(session, "Alice", ChatSession.DefaultPort);

        var result = await command.ExecuteAsync(arguments, TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.Contains("Usage: /status", result.Message);
    }
}

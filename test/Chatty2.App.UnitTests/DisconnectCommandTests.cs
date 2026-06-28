using System;
using System.Threading.Tasks;
using Chatty2.Core;
using NSubstitute;
using Xunit;

namespace Chatty2.App.UnitTests;

public class DisconnectCommandTests
{
    [Fact]
    public async Task Should_DisconnectSession_When_Connected()
    {
        var session = Substitute.For<IChatSession>();
        var command = new DisconnectCommand(session);

        var result = await command.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.False(result.IsError);
        Assert.Null(result.Message);
        session.Received(1).Disconnect();
    }

    [Fact]
    public async Task Should_ReturnFriendlyError_When_NotConnected()
    {
        var session = Substitute.For<IChatSession>();
        session.When(s => s.Disconnect()).Do(_ => throw new InvalidOperationException("Not connected to a peer."));
        var command = new DisconnectCommand(session);

        var result = await command.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.True(result.IsError);
        Assert.NotNull(result.Message);
        Assert.Contains("Not connected", result.Message);
    }

    [Fact]
    public async Task Should_IgnoreArguments_When_ExtraArgumentsPassed()
    {
        var session = Substitute.For<IChatSession>();
        var command = new DisconnectCommand(session);

        var result = await command.ExecuteAsync(["now", "please"], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        session.Received(1).Disconnect();
    }
}

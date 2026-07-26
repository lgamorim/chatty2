using Xunit;

namespace Chatty2.App.UnitTests;

public class HelpCommandTests
{
    [Fact]
    public async Task Should_ListAllCommandsAndMessageRule_When_Executed()
    {
        var command = new HelpCommand();

        var result = await command.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
        Assert.False(result.IsError);
        Assert.NotNull(result.Message);
        Assert.Contains("/connect", result.Message);
        Assert.Contains("/disconnect", result.Message);
        Assert.Contains("/help", result.Message);
        Assert.Contains("/exit", result.Message);
        Assert.Contains("Type a message", result.Message);
    }

    [Fact]
    public async Task Should_NotThrow_When_ExtraArgumentsPassed()
    {
        var command = new HelpCommand();

        var result = await command.ExecuteAsync(["foo"], TestContext.Current.CancellationToken);

        Assert.False(result.ShouldExit);
    }
}

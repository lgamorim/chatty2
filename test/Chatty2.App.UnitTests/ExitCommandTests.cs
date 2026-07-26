using Xunit;

namespace Chatty2.App.UnitTests;

public class ExitCommandTests
{
    [Fact]
    public async Task Should_ReturnShouldExitTrue_When_Executed()
    {
        var command = new ExitCommand();

        var result = await command.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.True(result.ShouldExit);
        Assert.False(result.IsError);
        Assert.Equal("Goodbye!", result.Message);
    }

    [Fact]
    public async Task Should_NotThrow_When_ExtraArgumentsPassed()
    {
        var command = new ExitCommand();

        var result = await command.ExecuteAsync(["now"], TestContext.Current.CancellationToken);

        Assert.True(result.ShouldExit);
    }
}

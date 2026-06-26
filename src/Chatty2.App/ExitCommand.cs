namespace Chatty2.App;

public sealed class ExitCommand : ICommand
{
    public string Name => "exit";

    public Task<CommandResult> ExecuteAsync(string[] arguments, CancellationToken cancellationToken)
    {
        return Task.FromResult(CommandResult.Exit("Goodbye!"));
    }
}

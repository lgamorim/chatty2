namespace Chatty2.App;

public interface ICommand
{
    string Name { get; }

    Task<CommandResult> ExecuteAsync(string[] arguments, CancellationToken cancellationToken);
}

namespace Chatty2.App;

public readonly record struct CommandResult(bool ShouldExit, string? Message, bool IsError)
{
    public static CommandResult Continue(string? message = null) => new(false, message, false);

    public static CommandResult Error(string message) => new(false, message, true);

    public static CommandResult Exit(string? message = null) => new(true, message, false);
}

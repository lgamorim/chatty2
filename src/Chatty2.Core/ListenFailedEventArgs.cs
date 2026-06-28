namespace Chatty2.Core;

public sealed class ListenFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}

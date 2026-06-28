namespace Chatty2.Core;

public sealed class ChatMessageReceivedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

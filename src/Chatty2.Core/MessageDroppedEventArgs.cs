namespace Chatty2.Core;

public sealed class MessageDroppedEventArgs(int maxLength) : EventArgs
{
    public int MaxLength { get; } = maxLength;
}

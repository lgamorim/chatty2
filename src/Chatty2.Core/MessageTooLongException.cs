namespace Chatty2.Core;

/// <summary>
/// A chat line exceeded the maximum length: either a peer sent one (the connection stays
/// usable and the offending line has already been discarded) or the local caller tried to.
/// </summary>
public sealed class MessageTooLongException(int maxLength)
    : Exception($"A message exceeded the maximum length of {maxLength} characters.")
{
    public int MaxLength { get; } = maxLength;
}

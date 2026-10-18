namespace Chatty2.Core;

public interface IPeerConnection : IDisposable
{
    string RemoteEndPoint { get; }

    Task SendAsync(string message, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the next line, or <c>null</c> once the stream has ended. Throws
    /// <see cref="MessageTooLongException"/> for a line over the length limit; that line has
    /// already been discarded, so the next call yields the following one.
    /// </summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}

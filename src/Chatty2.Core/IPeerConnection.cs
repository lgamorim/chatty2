namespace Chatty2.Core;

public interface IPeerConnection : IDisposable
{
    string RemoteEndPoint { get; }

    Task SendAsync(string message, CancellationToken cancellationToken);

    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
}

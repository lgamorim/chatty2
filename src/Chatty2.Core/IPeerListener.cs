namespace Chatty2.Core;

public interface IPeerListener
{
    Task<IPeerConnection> AcceptAsync(int port, CancellationToken cancellationToken);
}

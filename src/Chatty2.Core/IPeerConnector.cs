using System.Net;

namespace Chatty2.Core;

public interface IPeerConnector
{
    Task<IPeerConnection> ConnectAsync(IPAddress ipAddress, int port, CancellationToken cancellationToken);
}

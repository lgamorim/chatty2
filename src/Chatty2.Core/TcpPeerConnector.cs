using System.Net;
using System.Net.Sockets;

namespace Chatty2.Core;

public sealed class TcpPeerConnector : IPeerConnector
{
    public async Task<IPeerConnection> ConnectAsync(IPAddress ipAddress, int port, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);

        var client = new TcpClient();
        await client.ConnectAsync(ipAddress, port, cancellationToken);
        return new TcpPeerConnection(client);
    }
}

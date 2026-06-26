using System.Net;
using System.Net.Sockets;

namespace Chatty2.Core;

public sealed class TcpPeerListener : IPeerListener
{
    public async Task<IPeerConnection> AcceptAsync(int port, CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        try
        {
            var client = await listener.AcceptTcpClientAsync(cancellationToken);
            return new TcpPeerConnection(client);
        }
        finally
        {
            listener.Stop();
        }
    }
}

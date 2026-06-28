using System.Net.Sockets;
using System.Text;

namespace Chatty2.Core;

public sealed class TcpPeerConnection : IPeerConnection
{
    // Encoding.UTF8 emits a 3-byte BOM preamble before the first write; over a raw
    // socket there's no file/stream header convention expecting that, so use a UTF-8
    // encoding instance configured not to emit it.
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TcpClient client;
    private readonly StreamReader reader;
    private readonly StreamWriter writer;
    private bool disposed;

    public TcpPeerConnection(TcpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        this.client = client;
        var stream = client.GetStream();
        reader = new StreamReader(stream, Utf8WithoutBom);
        writer = new StreamWriter(stream, Utf8WithoutBom) { AutoFlush = true };
        RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
    }

    public string RemoteEndPoint { get; }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        return writer.WriteLineAsync(message.AsMemory(), cancellationToken);
    }

    public Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        return reader.ReadLineAsync(cancellationToken).AsTask();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            // Shutting down only the send direction lets the remote side observe a clean
            // end-of-stream (ReceiveAsync returning null) instead of an abortive-close
            // exception, while still allowing any in-flight inbound data to be drained.
            client.Client.Shutdown(SocketShutdown.Send);
        }
        catch (SocketException)
        {
            // The remote side may have already closed the connection; shutdown is best-effort.
        }

        writer.Dispose();
        reader.Dispose();
        client.Dispose();
    }
}

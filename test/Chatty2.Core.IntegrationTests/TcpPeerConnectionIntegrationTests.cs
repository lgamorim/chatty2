using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Chatty2.Core;
using Xunit;

namespace Chatty2.Core.IntegrationTests;

public class TcpPeerConnectionIntegrationTests
{
    [Fact]
    public async Task Should_DeliverMessage_When_ConnectorDialsListener()
    {
        var port = GetFreeLoopbackPort();
        var listener = new TcpPeerListener();
        var connector = new TcpPeerConnector();

        var acceptTask = listener.AcceptAsync(port, CancellationToken.None);
        using var clientConnection = await connector.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None);
        using var serverConnection = await acceptTask;

        await clientConnection.SendAsync("hello world", CancellationToken.None);
        var received = await serverConnection.ReceiveAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal("hello world", received);
    }

    [Fact]
    public async Task Should_ReturnNullFromReceiveAsync_When_RemotePeerDisposesConnection()
    {
        var port = GetFreeLoopbackPort();
        var listener = new TcpPeerListener();
        var connector = new TcpPeerConnector();

        var acceptTask = listener.AcceptAsync(port, CancellationToken.None);
        var clientConnection = await connector.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None);
        using var serverConnection = await acceptTask;

        clientConnection.Dispose();

        var received = await serverConnection.ReceiveAsync(CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Null(received);
    }

    [Fact]
    public async Task Should_ThrowSocketException_When_ConnectingToClosedPort()
    {
        var port = GetFreeLoopbackPort();
        var connector = new TcpPeerConnector();

        await Assert.ThrowsAsync<SocketException>(
            () => connector.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None));
    }

    [Fact]
    public async Task Should_ThrowWhenCancelled_When_AcceptAsyncCancelledBeforeAnyConnectionArrives()
    {
        var port = GetFreeLoopbackPort();
        var listener = new TcpPeerListener();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => listener.AcceptAsync(port, cts.Token));
    }

    [Fact]
    public async Task Should_PreserveLineBoundaries_When_SendingRapidConsecutiveMessages()
    {
        var port = GetFreeLoopbackPort();
        var listener = new TcpPeerListener();
        var connector = new TcpPeerConnector();

        var acceptTask = listener.AcceptAsync(port, CancellationToken.None);
        using var clientConnection = await connector.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None);
        using var serverConnection = await acceptTask;

        var messages = Enumerable.Range(1, 20).Select(i => $"message-{i}").ToArray();
        foreach (var message in messages)
        {
            await clientConnection.SendAsync(message, CancellationToken.None);
        }

        var receivedMessages = new List<string>();
        for (var i = 0; i < messages.Length; i++)
        {
            var received = await serverConnection.ReceiveAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            receivedMessages.Add(received!);
        }

        Assert.Equal(messages, receivedMessages);
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

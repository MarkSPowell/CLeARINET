namespace Clearinet.ProxyCore.WebSockets;

/// <summary>
/// Passes a WebSocket connection's bytes through in both directions after
/// the 101 response, exactly as they arrive, and reads the messages out of
/// them on the way (<see cref="WebSocketFrameReader"/>) into the session's
/// <see cref="WebSocketLog"/>. Reading never changes or delays what's sent:
/// each chunk is forwarded first, then read.
/// </summary>
public static class WebSocketRelay
{
    /// <summary>
    /// After one side closes, how long the other has to finish before the
    /// connection is given up, so a half-closed connection doesn't hang
    /// around for ever.
    /// </summary>
    public static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(5);

    public static async Task RelayAsync(
        Stream client, Stream server, WebSocketLog log, WebSocketCompression? compression, CancellationToken cancellationToken)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var sent = new WebSocketFrameReader(WebSocketDirection.Sent, log, compression);
        using var received = new WebSocketFrameReader(WebSocketDirection.Received, log, compression);

        var clientToServer = PumpAsync(client, server, sent, stop.Token);
        var serverToClient = PumpAsync(server, client, received, stop.Token);
        try
        {
            var first = await Task.WhenAny(clientToServer, serverToClient);
            var other = first == clientToServer ? serverToClient : clientToServer;
            if (await Task.WhenAny(other, Task.Delay(CloseGrace, cancellationToken)) != other)
            {
                await stop.CancelAsync();
            }

            await Task.WhenAll(clientToServer, serverToClient);
        }
        catch (Exception)
        {
            // A reset or a one-sided close is how most connections end here.
        }
        finally
        {
            log.Close();
        }
    }

    private static async Task PumpAsync(Stream from, Stream to, WebSocketFrameReader reader, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await from.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return;
            }

            await to.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            reader.Feed(buffer.AsSpan(0, read));
        }
    }
}

using System.Globalization;
using System.Text;

namespace Clearinet.ProxyCore.Http;

/// <summary>
/// Reads a single HTTP/1.1 request or response off a stream and decodes it
/// into a <see cref="CapturedRequest"/>/<see cref="CapturedResponse"/>
/// (chunked transfer-encoding already unwrapped, so the body is always the
/// logical content, never the wire framing around it).
///
/// Two ways to get a body, and the choice matters: <see cref="ReadBodyAsync"/>
/// buffers the whole thing into memory before returning it, which is the
/// only option once a breakpoint has decided to pause this exchange (an
/// edit needs the whole message at once). <see cref="RelayBodyAsync"/>
/// writes every byte to a destination stream as it arrives *and* returns
/// the same complete, decoded bytes for capture -- this is what lets an
/// ordinary, non-paused exchange reach the other side of the proxy
/// incrementally instead of only after it's entirely finished.
///
/// That distinction exists because of a real regression this project hit:
/// this reader used to always relay bytes live to a paired stream while it
/// read them, which made pausing-before-forwarding (breakpoints)
/// architecturally impossible, so an earlier pass here switched to
/// "buffer everything, then decide, then forward" for every message,
/// unconditionally. That's fine for an ordinary request/response. It's not
/// fine for a long-lived streamed response -- a chat reply arriving token
/// by token over a chunked connection that can stay open for tens of
/// seconds -- where "buffer everything first" means the client sees
/// nothing at all until generation is completely finished, which looks
/// exactly like the proxy breaking the app. <see cref="RelayBodyAsync"/>
/// restores live relaying for the common case (nothing about to pause);
/// <c>Clearinet.ProxyCore.Proxy.InterceptingProxyListener</c> is what
/// decides, per message, which one applies -- see its own remarks on
/// <c>PumpSessionsAsync</c>.
///
/// Scope for where the project is right now: Content-Length and chunked
/// transfer-encoding bodies are handled, since together they cover the
/// overwhelming majority of real HTTP/1.1 traffic. A response framed by
/// "read until the connection closes" (no Content-Length, no chunked
/// encoding -- legal but old-fashioned HTTP/1.0-style framing) is not
/// supported yet: it's rare on modern HTTPS traffic and would need this
/// reader to stop working message-by-message and switch the whole
/// connection over to a raw pump, which is a deliberate follow-up rather
/// than something to guess at here.
/// </summary>
public static class Http1MessageReader
{
    private const int MaxStartLineOrHeaderLength = 16 * 1024;

    // Read/relay granularity for a Content-Length-framed body. Chunked
    // bodies don't need this -- they already relay at their own natural
    // chunk boundaries -- but a large Content-Length body (a file download,
    // say) would otherwise have to be read in one single, fully-buffered
    // ReadExactAsync-style call even on the "relay live" path, which
    // defeats the entire point. 64KB is a plain middle ground: small
    // enough that relaying still feels live, large enough not to turn a
    // big download into a flood of tiny writes.
    private const int RelayBufferSize = 64 * 1024;

    /// <summary>
    /// Reads one request fully, body included. Returns <see langword="null"/>
    /// if the connection closed cleanly before another request arrived --
    /// the normal way a keep-alive HTTP/1.1 connection ends between
    /// messages.
    /// </summary>
    public static async Task<CapturedRequest?> ReadRequestAsync(Stream source, CancellationToken cancellationToken)
    {
        var preamble = await ReadRequestPreambleAsync(source, cancellationToken);
        if (preamble is null)
        {
            return null;
        }

        var body = await ReadBodyAsync(source, preamble.Headers, cancellationToken);
        return preamble with { Body = body };
    }

    /// <summary>
    /// Reads one response fully, body included. Returns <see langword="null"/>
    /// if the connection closed before a status line arrived at all -- the
    /// upstream giving up without answering.
    /// </summary>
    public static async Task<CapturedResponse?> ReadResponseAsync(
        Stream source, bool isResponseToHeadRequest, CancellationToken cancellationToken)
    {
        var preamble = await ReadResponsePreambleAsync(source, cancellationToken);
        if (preamble is null)
        {
            return null;
        }

        var body = ResponseHasNoBody(preamble.StatusCode, isResponseToHeadRequest)
            ? Array.Empty<byte>()
            : await ReadBodyAsync(source, preamble.Headers, cancellationToken);
        return preamble with { Body = body };
    }

    /// <summary>
    /// Reads just the request line and headers, with <c>Body</c> left as an
    /// empty placeholder. Lets a caller decide whether a breakpoint would
    /// actually apply -- <c>Clearinet.ProxyCore.Breakpoints.BreakpointRules.ShouldBreakBeforeRequest</c>
    /// only ever looks at <c>Method</c>/<c>Target</c>, never the body --
    /// before committing to <see cref="ReadBodyAsync"/> or
    /// <see cref="RelayBodyAsync"/> for what comes next.
    /// </summary>
    public static async Task<CapturedRequest?> ReadRequestPreambleAsync(Stream source, CancellationToken cancellationToken)
    {
        var startLine = await ReadLineAsync(source, allowImmediateEof: true, cancellationToken);
        if (startLine is null)
        {
            return null;
        }

        var parts = startLine.Split(' ', 3);
        if (parts.Length != 3)
        {
            throw new InvalidDataException($"Malformed request line: '{startLine}'");
        }

        var headers = await ReadHeadersAsync(source, cancellationToken);
        return new CapturedRequest(parts[0], parts[1], parts[2], headers, []);
    }

    /// <summary>Same idea as <see cref="ReadRequestPreambleAsync"/>, for the response stage.</summary>
    public static async Task<CapturedResponse?> ReadResponsePreambleAsync(Stream source, CancellationToken cancellationToken)
    {
        var startLine = await ReadLineAsync(source, allowImmediateEof: true, cancellationToken);
        if (startLine is null)
        {
            return null;
        }

        var parts = startLine.Split(' ', 3);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var statusCode))
        {
            throw new InvalidDataException($"Malformed status line: '{startLine}'");
        }

        var reason = parts.Length > 2 ? parts[2] : string.Empty;
        var headers = await ReadHeadersAsync(source, cancellationToken);
        return new CapturedResponse(parts[0], statusCode, reason, headers, []);
    }

    /// <summary>
    /// 1xx/204/304 and responses to HEAD never carry a body, whatever
    /// Content-Length or Transfer-Encoding claim -- reading (or relaying)
    /// one anyway would just hang waiting for bytes the server never
    /// sends. Public because both <see cref="ReadResponseAsync"/> and
    /// <c>InterceptingProxyListener.PumpSessionsAsync</c> need the same
    /// answer, the latter before it's committed to a buffer-or-relay path.
    /// </summary>
    public static bool ResponseHasNoBody(int statusCode, bool isResponseToHeadRequest) =>
        (statusCode is >= 100 and < 200 or 204 or 304) || isResponseToHeadRequest;

    /// <summary>
    /// Reads a body fully into memory. See the class remarks for when to
    /// use this instead of <see cref="RelayBodyAsync"/>.
    /// </summary>
    public static Task<byte[]> ReadBodyAsync(
        Stream source, IReadOnlyList<(string Name, string Value)> headers, CancellationToken cancellationToken)
    {
        var transferEncoding = FindHeader(headers, "Transfer-Encoding");
        if (transferEncoding is not null && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            return ReadChunkedBodyAsync(source, cancellationToken);
        }

        var contentLengthText = FindHeader(headers, "Content-Length");
        if (contentLengthText is not null)
        {
            if (!long.TryParse(contentLengthText, out var contentLength) || contentLength < 0)
            {
                throw new InvalidDataException($"Malformed Content-Length: '{contentLengthText}'");
            }

            return contentLength == 0
                ? Task.FromResult(Array.Empty<byte>())
                : ReadExactAsync(source, contentLength, cancellationToken);
        }

        // Neither header: for a request this is the normal shape of a
        // GET/DELETE/etc with no body. See the class remarks for why a
        // close-terminated response isn't handled yet.
        return Task.FromResult(Array.Empty<byte>());
    }

    /// <summary>
    /// Reads a body the same way <see cref="ReadBodyAsync"/> does, except
    /// every byte is written to <paramref name="destination"/> as it
    /// arrives instead of only being accumulated. The returned bytes are
    /// still the complete, decoded body -- built from the same bytes as
    /// they're relayed, not read twice -- so session capture works
    /// identically either way. See the class remarks for why this exists.
    /// </summary>
    public static Task<byte[]> RelayBodyAsync(
        Stream source, Stream destination, IReadOnlyList<(string Name, string Value)> headers, CancellationToken cancellationToken)
    {
        var transferEncoding = FindHeader(headers, "Transfer-Encoding");
        if (transferEncoding is not null && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            return RelayChunkedBodyAsync(source, destination, cancellationToken);
        }

        var contentLengthText = FindHeader(headers, "Content-Length");
        if (contentLengthText is not null)
        {
            if (!long.TryParse(contentLengthText, out var contentLength) || contentLength < 0)
            {
                throw new InvalidDataException($"Malformed Content-Length: '{contentLengthText}'");
            }

            return contentLength == 0
                ? Task.FromResult(Array.Empty<byte>())
                : RelayExactBytesAsync(source, destination, contentLength, cancellationToken);
        }

        return Task.FromResult(Array.Empty<byte>());
    }

    private static async Task<List<(string Name, string Value)>> ReadHeadersAsync(
        Stream source, CancellationToken cancellationToken)
    {
        var headers = new List<(string, string)>();
        while (true)
        {
            var line = await ReadLineAsync(source, allowImmediateEof: false, cancellationToken);
            if (line!.Length == 0)
            {
                break;
            }

            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                throw new InvalidDataException($"Malformed header line: '{line}'");
            }

            headers.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }

        return headers;
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(Stream source, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var sizeLine = await ReadLineAsync(source, allowImmediateEof: false, cancellationToken);
            var semicolon = sizeLine!.IndexOf(';');
            var sizeText = semicolon >= 0 ? sizeLine[..semicolon] : sizeLine;
            if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size) || size < 0)
            {
                throw new InvalidDataException($"Malformed chunk size: '{sizeLine}'");
            }

            if (size == 0)
            {
                // Optional trailer headers, then the final blank line.
                // Rare in practice; drained and discarded rather than
                // surfaced, since nothing downstream reads trailers yet.
                while (true)
                {
                    var trailerLine = await ReadLineAsync(source, allowImmediateEof: false, cancellationToken);
                    if (trailerLine!.Length == 0)
                    {
                        break;
                    }
                }

                break;
            }

            var chunk = await ReadExactAsync(source, size, cancellationToken);
            body.Write(chunk);

            var terminator = await ReadLineAsync(source, allowImmediateEof: false, cancellationToken);
            if (terminator!.Length != 0)
            {
                throw new InvalidDataException("Malformed chunk terminator.");
            }
        }

        return body.ToArray();
    }

    /// <summary>
    /// Same chunk-by-chunk walk as <see cref="ReadChunkedBodyAsync"/>, but
    /// every line and every chunk's bytes are written to
    /// <paramref name="destination"/> immediately after being read --
    /// chunk framing included, so the wire format this relays is exactly
    /// what a real client already expects from a chunked response, not a
    /// rebuilt approximation of it.
    /// </summary>
    private static async Task<byte[]> RelayChunkedBodyAsync(
        Stream source, Stream destination, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var sizeLine = await ReadLineAsync(source, allowImmediateEof: false, cancellationToken);
            await WriteLineAsync(destination, sizeLine!, cancellationToken);

            var semicolon = sizeLine!.IndexOf(';');
            var sizeText = semicolon >= 0 ? sizeLine[..semicolon] : sizeLine;
            if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size) || size < 0)
            {
                throw new InvalidDataException($"Malformed chunk size: '{sizeLine}'");
            }

            if (size == 0)
            {
                while (true)
                {
                    var trailerLine = await ReadLineAsync(source, allowImmediateEof: false, cancellationToken);
                    await WriteLineAsync(destination, trailerLine!, cancellationToken);
                    if (trailerLine!.Length == 0)
                    {
                        break;
                    }
                }

                break;
            }

            var chunk = await ReadExactAsync(source, size, cancellationToken);
            await destination.WriteAsync(chunk, cancellationToken);
            body.Write(chunk);

            var terminator = await ReadLineAsync(source, allowImmediateEof: false, cancellationToken);
            await WriteLineAsync(destination, terminator ?? string.Empty, cancellationToken);
            if (terminator!.Length != 0)
            {
                throw new InvalidDataException("Malformed chunk terminator.");
            }
        }

        return body.ToArray();
    }

    /// <summary>
    /// Relays a Content-Length-framed body in <see cref="RelayBufferSize"/>
    /// increments rather than one fully-buffered read -- the whole point
    /// of this method is that a large body still streams through instead
    /// of sitting entirely in memory before the first byte moves on.
    /// </summary>
    private static async Task<byte[]> RelayExactBytesAsync(
        Stream source, Stream destination, long count, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        var buffer = new byte[(int)Math.Min(count, RelayBufferSize)];
        var remaining = count;

        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0)
            {
                throw new InvalidDataException("Connection closed before the expected body was fully read.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            body.Write(buffer, 0, read);
            remaining -= read;
        }

        return body.ToArray();
    }

    private static ValueTask WriteLineAsync(Stream destination, string line, CancellationToken cancellationToken) =>
        destination.WriteAsync(Encoding.ASCII.GetBytes(line + "\r\n"), cancellationToken);

    private static string? FindHeader(IReadOnlyList<(string Name, string Value)> headers, string name)
    {
        foreach (var (headerName, value) in headers)
        {
            if (string.Equals(headerName, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static async Task<byte[]> ReadExactAsync(Stream source, long count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await source.ReadAsync(buffer.AsMemory(offset, (int)(count - offset)), cancellationToken);
            if (read == 0)
            {
                throw new InvalidDataException("Connection closed before the expected body was fully read.");
            }

            offset += read;
        }

        return buffer;
    }

    // Byte-at-a-time on purpose, same tradeoff as the CONNECT-preamble
    // reader this builds on: start lines, header lines and chunk-size lines
    // are always small, so simplicity wins over throughput here.
    //
    // allowImmediateEof controls what an EOF on the very first byte means:
    // true for the start of a request/response (a clean, expected end of a
    // keep-alive connection -- returns null), false everywhere else (a
    // truncated message -- throws).
    private static async Task<string?> ReadLineAsync(
        Stream source, bool allowImmediateEof, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var single = new byte[1];

        while (true)
        {
            var read = await source.ReadAsync(single.AsMemory(), cancellationToken);
            if (read == 0)
            {
                if (bytes.Count == 0 && allowImmediateEof)
                {
                    return null;
                }

                throw new InvalidDataException("Connection closed unexpectedly while reading a line.");
            }

            if (single[0] == (byte)'\n')
            {
                break;
            }

            if (single[0] != (byte)'\r')
            {
                bytes.Add(single[0]);
                if (bytes.Count > MaxStartLineOrHeaderLength)
                {
                    throw new InvalidDataException("A line exceeded the maximum allowed length.");
                }
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }
}

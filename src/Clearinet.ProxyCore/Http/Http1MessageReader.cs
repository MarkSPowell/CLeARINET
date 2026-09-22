using System.Globalization;
using System.Text;

namespace Clearinet.ProxyCore.Http;

/// <summary>
/// Reads a single HTTP/1.1 request or response off a stream, relaying every
/// byte to a paired stream as it goes -- so capturing a message never
/// changes what the other side of the tunnel sees, and the bytes we hand
/// off downstream are the exact original wire bytes, not a re-serialization
/// of whatever we understood -- while also decoding headers and body into a
/// <see cref="CapturedRequest"/>/<see cref="CapturedResponse"/> for
/// inspection.
///
/// Scope for where the project is right now: Content-Length and chunked
/// transfer-encoding bodies are handled, since together they cover the
/// overwhelming majority of real HTTP/1.1 traffic. A response framed by
/// "read until the connection closes" (no Content-Length, no chunked
/// encoding -- legal but old-fashioned HTTP/1.0-style framing) is not
/// supported yet: it's rare on modern HTTPS traffic and would need this
/// reader to stop relaying request-by-request and switch the whole
/// connection over to a raw pump, which is a deliberate follow-up rather
/// than something to guess at here.
/// </summary>
public static class Http1MessageReader
{
    private const int MaxStartLineOrHeaderLength = 16 * 1024;

    /// <summary>
    /// Reads one request. Returns <see langword="null"/> if the connection
    /// closed cleanly before another request arrived -- the normal way a
    /// keep-alive HTTP/1.1 connection ends between messages.
    /// </summary>
    public static async Task<CapturedRequest?> ReadRequestAsync(
        Stream source, Stream relayTo, CancellationToken cancellationToken)
    {
        var startLine = await ReadLineAsync(source, relayTo, allowImmediateEof: true, cancellationToken);
        if (startLine is null)
        {
            return null;
        }

        var parts = startLine.Split(' ', 3);
        if (parts.Length != 3)
        {
            throw new InvalidDataException($"Malformed request line: '{startLine}'");
        }

        var headers = await ReadHeadersAsync(source, relayTo, cancellationToken);
        var body = await ReadBodyAsync(source, relayTo, headers, cancellationToken);

        return new CapturedRequest(parts[0], parts[1], parts[2], headers, body);
    }

    /// <summary>
    /// Reads one response. Returns <see langword="null"/> if the connection
    /// closed before a status line arrived at all -- the upstream giving up
    /// without answering.
    /// </summary>
    public static async Task<CapturedResponse?> ReadResponseAsync(
        Stream source, Stream relayTo, bool isResponseToHeadRequest, CancellationToken cancellationToken)
    {
        var startLine = await ReadLineAsync(source, relayTo, allowImmediateEof: true, cancellationToken);
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
        var headers = await ReadHeadersAsync(source, relayTo, cancellationToken);

        // 1xx/204/304 and responses to HEAD never carry a body, whatever
        // Content-Length or Transfer-Encoding claim -- reading one anyway
        // would just hang waiting for bytes the server never sends.
        var hasNoBody = (statusCode is >= 100 and < 200 or 204 or 304) || isResponseToHeadRequest;
        var body = hasNoBody
            ? Array.Empty<byte>()
            : await ReadBodyAsync(source, relayTo, headers, cancellationToken);

        return new CapturedResponse(parts[0], statusCode, reason, headers, body);
    }

    private static async Task<List<(string Name, string Value)>> ReadHeadersAsync(
        Stream source, Stream relayTo, CancellationToken cancellationToken)
    {
        var headers = new List<(string, string)>();
        while (true)
        {
            var line = await ReadLineAsync(source, relayTo, allowImmediateEof: false, cancellationToken);
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

    private static async Task<byte[]> ReadBodyAsync(
        Stream source, Stream relayTo, List<(string Name, string Value)> headers, CancellationToken cancellationToken)
    {
        var transferEncoding = FindHeader(headers, "Transfer-Encoding");
        if (transferEncoding is not null && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadChunkedBodyAsync(source, relayTo, cancellationToken);
        }

        var contentLengthText = FindHeader(headers, "Content-Length");
        if (contentLengthText is not null)
        {
            if (!long.TryParse(contentLengthText, out var contentLength) || contentLength < 0)
            {
                throw new InvalidDataException($"Malformed Content-Length: '{contentLengthText}'");
            }

            return contentLength == 0
                ? Array.Empty<byte>()
                : await ReadExactAsync(source, relayTo, contentLength, cancellationToken);
        }

        // Neither header: for a request this is the normal shape of a
        // GET/DELETE/etc with no body. See the class remarks for why a
        // close-terminated response isn't handled yet.
        return Array.Empty<byte>();
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(
        Stream source, Stream relayTo, CancellationToken cancellationToken)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var sizeLine = await ReadLineAsync(source, relayTo, allowImmediateEof: false, cancellationToken);
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
                    var trailerLine = await ReadLineAsync(source, relayTo, allowImmediateEof: false, cancellationToken);
                    if (trailerLine!.Length == 0)
                    {
                        break;
                    }
                }

                break;
            }

            var chunk = await ReadExactAsync(source, relayTo, size, cancellationToken);
            body.Write(chunk);

            var terminator = await ReadLineAsync(source, relayTo, allowImmediateEof: false, cancellationToken);
            if (terminator!.Length != 0)
            {
                throw new InvalidDataException("Malformed chunk terminator.");
            }
        }

        return body.ToArray();
    }

    private static string? FindHeader(List<(string Name, string Value)> headers, string name)
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

    private static async Task<byte[]> ReadExactAsync(
        Stream source, Stream relayTo, long count, CancellationToken cancellationToken)
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

            await relayTo.WriteAsync(buffer.AsMemory(offset, read), cancellationToken);
            offset += read;
        }

        return buffer;
    }

    // Byte-at-a-time on purpose, same tradeoff as the CONNECT-preamble
    // reader this builds on: start lines, header lines and chunk-size lines
    // are always small, so simplicity wins over throughput here. Every byte
    // is relayed to the other side the instant it's read, before the line
    // is even known to be complete, so capturing never adds latency the
    // other side would notice.
    //
    // allowImmediateEof controls what an EOF on the very first byte means:
    // true for the start of a request/response (a clean, expected end of a
    // keep-alive connection -- returns null), false everywhere else (a
    // truncated message -- throws).
    private static async Task<string?> ReadLineAsync(
        Stream source, Stream relayTo, bool allowImmediateEof, CancellationToken cancellationToken)
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

            await relayTo.WriteAsync(single.AsMemory(0, 1), cancellationToken);

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

using System.Globalization;
using System.Text;

namespace Clearinet.ProxyCore.Http;

/// <summary>
/// Serializes a <see cref="CapturedRequest"/>/<see cref="CapturedResponse"/>
/// back onto the wire (or into a byte array). Extracted out of
/// Clearinet.Sessions.SazWriter, which had this exact header-rebuilding
/// logic for its own reasons (a SAZ file needs a plain HTTP/1.x message per
/// entry, not the original wire framing);
/// <c>Clearinet.ProxyCore.Proxy.InterceptingProxyListener</c> needs the
/// identical rebuild for a different reason -- see
/// <see cref="Http1MessageReader"/>'s remarks on why messages are read and
/// re-serialized as two separate steps now instead of relayed live.
///
/// Always rebuilds Content-Length from the body actually being sent and
/// always drops Transfer-Encoding, regardless of what the original message
/// (or a breakpoint edit) claimed: the body here is already fully decoded,
/// so there's no chunked framing left to preserve, and trusting a
/// possibly-stale or hand-edited Content-Length is exactly the kind of
/// thing that produces a message the receiving side hangs on.
///
/// That rebuild only makes sense once the whole body is already known,
/// though -- exactly the case <see cref="Http1MessageReader.RelayBodyAsync"/>
/// exists to avoid for an exchange nothing is going to pause. For that
/// path, <see cref="WriteRequestPreambleAsync"/>/<see cref="WriteResponsePreambleAsync"/>
/// write the original headers through completely unchanged (original
/// Content-Length or Transfer-Encoding: chunked, whichever it was), since
/// the body bytes that follow are about to be relayed with that exact same
/// original framing, not rebuilt.
/// </summary>
public static class HttpMessageWriter
{
    public static void WriteRequest(Stream destination, CapturedRequest request) =>
        destination.Write(BuildRequestBytes(request));

    public static void WriteResponse(Stream destination, CapturedResponse response) =>
        destination.Write(BuildResponseBytes(response));

    public static Task WriteRequestAsync(Stream destination, CapturedRequest request, CancellationToken cancellationToken) =>
        destination.WriteAsync(BuildRequestBytes(request), cancellationToken).AsTask();

    public static Task WriteResponseAsync(Stream destination, CapturedResponse response, CancellationToken cancellationToken) =>
        destination.WriteAsync(BuildResponseBytes(response), cancellationToken).AsTask();

    /// <summary>
    /// Writes a request's start line and headers exactly as given -- no
    /// Content-Length/Transfer-Encoding rewriting. Only meaningful paired
    /// with <see cref="Http1MessageReader.RelayBodyAsync"/> immediately
    /// after: this only writes what the preamble claims, so whatever body
    /// framing it declares has to be honored by whatever's sent next.
    /// </summary>
    public static Task WriteRequestPreambleAsync(Stream destination, CapturedRequest request, CancellationToken cancellationToken) =>
        WritePreambleAsync(destination, $"{request.Method} {request.Target} {request.HttpVersion}", request.Headers, cancellationToken);

    /// <summary>Same idea as <see cref="WriteRequestPreambleAsync"/>, for the response stage.</summary>
    public static Task WriteResponsePreambleAsync(Stream destination, CapturedResponse response, CancellationToken cancellationToken)
    {
        var reason = string.IsNullOrEmpty(response.ReasonPhrase) ? string.Empty : $" {response.ReasonPhrase}";
        return WritePreambleAsync(destination, $"{response.HttpVersion} {response.StatusCode}{reason}", response.Headers, cancellationToken);
    }

    public static byte[] BuildRequestBytes(CapturedRequest request) =>
        BuildMessageBytes($"{request.Method} {request.Target} {request.HttpVersion}", request.Headers, request.Body);

    public static byte[] BuildResponseBytes(CapturedResponse response)
    {
        var reason = string.IsNullOrEmpty(response.ReasonPhrase) ? string.Empty : $" {response.ReasonPhrase}";
        return BuildMessageBytes($"{response.HttpVersion} {response.StatusCode}{reason}", response.Headers, response.Body);
    }

    private static byte[] BuildMessageBytes(
        string startLine, IReadOnlyList<(string Name, string Value)> headers, byte[] body)
    {
        var text = new StringBuilder();
        text.Append(startLine).Append("\r\n");

        var sawContentLength = false;
        foreach (var (name, value) in headers)
        {
            if (string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                sawContentLength = true;
                text.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                continue;
            }

            text.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        if (!sawContentLength && body.Length > 0)
        {
            text.Append("Content-Length: ").Append(body.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }

        text.Append("\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(text.ToString());
        var result = new byte[headerBytes.Length + body.Length];
        headerBytes.CopyTo(result, 0);
        body.CopyTo(result, headerBytes.Length);
        return result;
    }

    private static Task WritePreambleAsync(
        Stream destination, string startLine, IReadOnlyList<(string Name, string Value)> headers, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        text.Append(startLine).Append("\r\n");
        foreach (var (name, value) in headers)
        {
            text.Append(name).Append(": ").Append(value).Append("\r\n");
        }

        text.Append("\r\n");
        return destination.WriteAsync(Encoding.ASCII.GetBytes(text.ToString()), cancellationToken).AsTask();
    }
}

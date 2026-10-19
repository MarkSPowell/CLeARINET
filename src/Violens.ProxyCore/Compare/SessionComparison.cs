using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Violens.ProxyCore.Http;
using Violens.ProxyCore.Sessions;

namespace Violens.ProxyCore.Compare;

/// <summary>How Compare Sessions shows each message before comparing.</summary>
/// <param name="DecodeBodies">Decompress bodies (gzip, Brotli...), so the content is compared, not the compressed bytes.</param>
/// <param name="FormatBodies">Put JSON one value per line, and form fields one per line, so a difference shows on its own line.</param>
/// <param name="SortHeaders">Compare headers in name order, so the same headers sent in another order don't count as different.</param>
public sealed record SessionCompareOptions(bool DecodeBodies = true, bool FormatBodies = true, bool SortHeaders = false);

/// <summary>The two sides' requests and responses, compared.</summary>
public sealed record SessionComparisonResult(DiffResult Request, DiffResult Response)
{
    public int Differences => Request.Differences + Response.Differences;
}

/// <summary>
/// Compare Sessions: two sessions' requests and responses side by side.
/// Fiddler Classic handed both to an external diff program; this compares
/// them itself, as text: the request line (with the full URL) or status
/// line, the headers, then the body as the Raw inspector shows it.
/// Binary bodies are shown as a hex dump, or as a size and SHA-256 hash
/// when large.
/// </summary>
public static class SessionComparison
{
    /// <summary>Binary bodies up to this size are compared as a hex dump; larger ones by size and hash.</summary>
    public const int MaxHexDumpBytes = 64 * 1024;

    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SessionComparisonResult Compare(Session left, Session right, SessionCompareOptions? options = null)
    {
        options ??= new SessionCompareOptions();
        return new SessionComparisonResult(
            TextDiff.Compare(RequestText(left, options), RequestText(right, options)),
            TextDiff.Compare(ResponseText(left.Response, options), ResponseText(right.Response, options)));
    }

    /// <summary>The request as compared: request line with the full URL, headers, blank line, body.</summary>
    public static string RequestText(Session session, SessionCompareOptions options)
    {
        var request = session.Request;
        return MessageText($"{request.Method} {SessionUrl.Of(session)} {request.HttpVersion}", request.Headers, request.Body, options);
    }

    /// <summary>The response as compared: status line, headers, blank line, body.</summary>
    public static string ResponseText(CapturedResponse response, SessionCompareOptions options) =>
        MessageText($"{response.HttpVersion} {response.StatusCode} {response.ReasonPhrase}".TrimEnd(), response.Headers, response.Body, options);

    private static string MessageText(
        string startLine, IReadOnlyList<(string Name, string Value)> headers, byte[] body, SessionCompareOptions options)
    {
        var text = new StringBuilder(startLine).Append('\n');
        IEnumerable<(string Name, string Value)> ordered = options.SortHeaders
            ? headers.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            : headers;
        foreach (var (name, value) in ordered)
        {
            text.Append(name).Append(": ").Append(value).Append('\n');
        }

        if (body.Length > 0)
        {
            text.Append('\n').Append(BodyText(body, headers, options));
        }

        return text.ToString();
    }

    /// <summary>The body as compared (see <see cref="SessionCompareOptions"/>).</summary>
    public static string BodyText(byte[] body, IReadOnlyList<(string Name, string Value)> headers, SessionCompareOptions options)
    {
        var bytes = body;
        var note = string.Empty;
        var stillEncoded = false;
        var encoding = ContentDecoder.FindHeader(headers, "Content-Encoding");
        if (!string.IsNullOrWhiteSpace(encoding) && !string.Equals(encoding.Trim(), "identity", StringComparison.OrdinalIgnoreCase))
        {
            if (!options.DecodeBodies)
            {
                stillEncoded = true;
            }
            else if (ContentDecoder.TryDecode(body, headers, out var decoded, out var error))
            {
                bytes = decoded;
            }
            else
            {
                stillEncoded = true;
                note = $"[Compared as received ({encoding}): {error}]\n";
            }
        }

        // Compressed bytes are binary, whatever the Content-Type says.
        var contentType = ContentDecoder.FindHeader(headers, "Content-Type");
        if (stillEncoded || !IsText(bytes, contentType))
        {
            return note + BinaryText(bytes);
        }

        var text = ContentDecoder.DecodeText(bytes, contentType);
        if (options.FormatBodies)
        {
            text = Format(text, ContentDecoder.MediaType(contentType));
        }

        return note + text;
    }

    /// <summary>
    /// JSON indented one value per line; a form (<c>application/x-www-form-urlencoded</c>)
    /// one field per line. Anything else, or JSON that doesn't parse, as it is.
    /// </summary>
    public static string Format(string text, string mediaType)
    {
        if (mediaType == "application/x-www-form-urlencoded")
        {
            return string.Join('\n', text.Split('&'));
        }

        var trimmed = text.TrimStart();
        var looksLikeJson = mediaType == "application/json" || mediaType.EndsWith("+json", StringComparison.Ordinal) ||
                            (trimmed.StartsWith('{') || trimmed.StartsWith('['));
        if (!looksLikeJson)
        {
            return text;
        }

        try
        {
            return JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true })
                       ?.ToJsonString(IndentedJson)
                   ?? text;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return text; // not JSON after all, or JSON this parser won't take (duplicate keys, too deep)
        }
    }

    private static bool IsText(byte[] bytes, string? contentType)
    {
        if (HarWriter.IsText(contentType))
        {
            return true;
        }

        // No Content-Type, or one that says nothing (octet-stream is often
        // used for text too): text if it's valid UTF-8 with no NULs.
        var mediaType = ContentDecoder.MediaType(contentType);
        if (mediaType.Length > 0 && mediaType != "application/octet-stream")
        {
            return false;
        }

        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            return false;
        }

        try
        {
            _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string BinaryText(byte[] bytes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var summary = $"[Binary body: {bytes.Length:N0} bytes, SHA-256 {hash}]";
        return bytes.Length > MaxHexDumpBytes ? summary : summary + "\n" + HexDump(bytes);
    }

    /// <summary>16 bytes per line: offset, hex, and the printable ASCII characters.</summary>
    public static string HexDump(byte[] bytes)
    {
        var text = new StringBuilder(bytes.Length * 5);
        for (var offset = 0; offset < bytes.Length; offset += 16)
        {
            var count = Math.Min(16, bytes.Length - offset);
            text.Append(offset.ToString("X8", System.Globalization.CultureInfo.InvariantCulture)).Append("  ");
            for (var i = 0; i < 16; i++)
            {
                text.Append(i < count ? bytes[offset + i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + " " : "   ");
            }

            text.Append(' ');
            for (var i = 0; i < count; i++)
            {
                var b = bytes[offset + i];
                text.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            if (offset + 16 < bytes.Length)
            {
                text.Append('\n');
            }
        }

        return text.ToString();
    }
}

using System.IO.Compression;
using System.Text;
using ZstdSharp;

namespace Clearinet.ProxyCore.Http;

/// <summary>
/// Undoes a message body's Content-Encoding and turns it into text: shared
/// by the inspectors (Raw, JSON, WebForms, ImageView) and by HAR export,
/// which both need the body as the application sees it rather than as it
/// crossed the wire.
///
/// Handles every Content-Encoding actually seen on the modern web: gzip
/// (and its rare "x-gzip" alias), deflate, br (Brotli, via .NET's own
/// System.IO.Compression), and zstd (via ZstdSharp.Port, a pure managed
/// port -- .NET doesn't ship a built-in zstd decoder until .NET 11). "deflate"
/// is decoded as zlib-wrapped data (RFC 1950) first, falling back to raw
/// DEFLATE (RFC 1951), since both exist in the wild under that one name. A
/// Content-Encoding header can list more than one coding applied in
/// sequence (RFC 9110 section 8.4, e.g. "gzip, br"); those are undone in
/// reverse, most recently applied first.
///
/// A few codings are recognized by name but never decoded -- see
/// <see cref="KnownUnsupportedEncodings"/> for which and why. Anything else
/// unrecognized is treated as undeclared and passed through as-is.
/// </summary>
public static class ContentDecoder
{
    /// <summary>
    /// Content-Encoding tokens recognized by name but never decoded, each
    /// paired with the reason a user sees if they hit one. Checked
    /// case-insensitively.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownUnsupportedEncodings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bzip2"] = "bzip2 was never a registered HTTP Content-Encoding, and no mainstream browser or " +
                        "server sends it. Support is intentionally left out unless a real site turns up that " +
                        "needs it.",
            ["compress"] = "the original LZW-based Content-Encoding, obsolete since the 1990s and not sent by " +
                            "anything in current use.",
            ["sdch"] = "SDCH (Shared Dictionary Compression over HTTP) compressed a body against a per-origin " +
                       "dictionary negotiated separately; without having captured that exact dictionary, the " +
                       "body can't be decoded even in principle, so this isn't a gap that more codec support " +
                       "would close. Chrome removed SDCH in 2016 and it won't appear on the modern web, but the " +
                       "name is recognized here so a capture against something ancient still gets an " +
                       "explanation instead of garbage text.",
        };

    /// <summary>
    /// Undoes <paramref name="contentEncodingHeader"/> on <paramref name="body"/>.
    /// Never throws for bad input: returns false with a readable
    /// <paramref name="error"/> instead, and <paramref name="decoded"/> left
    /// as the original bytes.
    /// </summary>
    public static bool TryDecode(byte[] body, string? contentEncodingHeader, out byte[] decoded, out string? error)
    {
        decoded = body;
        error = null;
        if (body.Length == 0 || string.IsNullOrWhiteSpace(contentEncodingHeader))
        {
            return true;
        }

        var tokens = contentEncodingHeader.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var current = body;
        for (var i = tokens.Length - 1; i >= 0; i--)
        {
            var token = tokens[i];
            if (string.Equals(token, "identity", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (KnownUnsupportedEncodings.TryGetValue(token, out var reason))
            {
                error = $"Body is {token}-compressed. {reason} See the Hex tab for the raw bytes.";
                return false;
            }

            if (!TryDecodeOne(current, token, out current, out var decodeError))
            {
                error = decodeError;
                return false;
            }
        }

        decoded = current;
        return true;
    }

    /// <summary>
    /// <see cref="TryDecode(byte[], string?, out byte[], out string?)"/> using
    /// the Content-Encoding found in <paramref name="headers"/>.
    /// </summary>
    public static bool TryDecode(
        byte[] body, IReadOnlyList<(string Name, string Value)> headers, out byte[] decoded, out string? error) =>
        TryDecode(body, FindHeader(headers, "Content-Encoding"), out decoded, out error);

    /// <summary>
    /// The body as text, using the charset from <paramref name="contentType"/>
    /// when it names one .NET knows, otherwise UTF-8 (which covers nearly all
    /// real traffic and degrades to replacement characters, never an
    /// exception). A UTF-8 byte order mark is dropped.
    /// </summary>
    public static string DecodeText(byte[] bytes, string? contentType)
    {
        var encoding = EncodingFor(contentType);
        var text = encoding.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    /// <summary>The media type from a Content-Type value, lower-cased, without parameters ("" when absent).</summary>
    public static string MediaType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return string.Empty;
        }

        var semicolon = contentType.IndexOf(';');
        return (semicolon < 0 ? contentType : contentType[..semicolon]).Trim().ToLowerInvariant();
    }

    /// <summary>First header named <paramref name="name"/>, case-insensitively, or null.</summary>
    public static string? FindHeader(IReadOnlyList<(string Name, string Value)> headers, string name)
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

    private static Encoding EncodingFor(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return Encoding.UTF8;
        }

        foreach (var parameter in contentType.Split(';').Skip(1))
        {
            var equals = parameter.IndexOf('=');
            if (equals < 0 || !string.Equals(parameter[..equals].Trim(), "charset", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var charset = parameter[(equals + 1)..].Trim().Trim('"');
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                return Encoding.UTF8;
            }
        }

        return Encoding.UTF8;
    }

    private static bool TryDecodeOne(byte[] input, string token, out byte[] output, out string? error)
    {
        error = null;
        try
        {
            output = token.ToLowerInvariant() switch
            {
                "gzip" or "x-gzip" => ReadAllFrom(new GZipStream(new MemoryStream(input), CompressionMode.Decompress)),
                "deflate" => InflateDeflate(input),
                "br" => ReadAllFrom(new BrotliStream(new MemoryStream(input), CompressionMode.Decompress)),
                "zstd" => ReadAllFrom(new DecompressionStream(new MemoryStream(input))),
                _ => input,
            };
            return true;
        }
        catch (Exception ex)
        {
            // Deliberately broad: ZstdSharp doesn't document a closed set of
            // exception types for malformed input. Most likely causes: the
            // body claims an encoding it isn't in, or it's truncated.
            output = input;
            error = $"Couldn't decompress the body as \"{token}\": {ex.Message}";
            return false;
        }
    }

    private static byte[] InflateDeflate(byte[] input)
    {
        try
        {
            return ReadAllFrom(new ZLibStream(new MemoryStream(input), CompressionMode.Decompress));
        }
        catch (InvalidDataException)
        {
            return ReadAllFrom(new DeflateStream(new MemoryStream(input), CompressionMode.Decompress));
        }
    }

    private static byte[] ReadAllFrom(Stream decompressor)
    {
        using (decompressor)
        {
            using var destination = new MemoryStream();
            decompressor.CopyTo(destination);
            return destination.ToArray();
        }
    }
}

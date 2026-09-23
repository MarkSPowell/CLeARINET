using System.IO.Compression;
using System.Text;
using ZstdSharp;

namespace Clearinet.Extensibility.Inspection.Inspectors;

/// <summary>
/// Fiddler Classic's "TextView", named "Raw" here since that's this first
/// cut's actual job: get from wire bytes to readable text, decompressed --
/// no pretty-printing or syntax highlighting yet (see <see cref="TextContent.SyntaxHint"/>
/// for where that would plug in later).
///
/// Handles every Content-Encoding actually seen on the modern web: gzip
/// (and its rare "x-gzip" alias), deflate, br (Brotli, via .NET's own
/// System.IO.Compression), and zstd (via ZstdSharp.Port, a pure managed
/// port -- .NET doesn't ship a built-in zstd decoder until .NET 11, per
/// Microsoft's own .NET 11 release notes, and this repo targets .NET 10; a
/// managed port avoids shipping a native binary per platform for the one
/// codec that gap leaves out). "deflate" is decoded as zlib-wrapped data
/// (RFC 1950) first, falling back to raw DEFLATE (RFC 1951) -- see
/// <see cref="InflateDeflate"/> for why both exist in the wild under this
/// one name. A Content-Encoding header can also list more than one coding
/// applied in sequence (RFC 9110 section 8.4, e.g. "gzip, br"); those are
/// undone in reverse, most-recently-applied first.
///
/// A few codings are recognized by name but never decoded -- see
/// <see cref="KnownUnsupportedEncodings"/> for which ones and why each is a
/// deliberate call rather than a gap. Zopfli isn't a Content-Encoding at
/// all (it's a slower, denser gzip-compatible *encoder*, useful only once
/// something -- AutoResponder, Composer -- is generating response bodies
/// rather than just reading them), so there's nothing for this inspector to
/// do with it; it belongs with whichever future feature writes bodies.
/// Anything else unrecognized is treated as undeclared and shown as-is,
/// same as always.
/// </summary>
public sealed class RawTextInspector : IInspector
{
    /// <summary>
    /// Content-Encoding tokens recognized by name but never decoded, each
    /// paired with the reason a user sees if they hit one -- so a body
    /// stuck this way reads as a deliberate design call, not a bug or a
    /// forgotten codec. Checked case-insensitively.
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

    public string Id => "clearinet.raw";

    public string DisplayName => "Raw";

    public int SortOrder => 10;

    public bool CanInspect(InspectorContext context) => true;

    public InspectorContent Inspect(InspectorContext context)
    {
        if (context.Body.Length == 0)
        {
            return new TextContent(string.Empty);
        }

        if (!TryDecode(context.Body, context.FindHeader("Content-Encoding"), out var decoded, out var error))
        {
            return new ErrorContent(error!);
        }

        // No charset sniffing from Content-Type yet -- a real, tracked gap.
        // UTF-8 covers the overwhelming majority of real traffic and
        // degrades gracefully (replacement characters, not an exception)
        // for anything it can't decode cleanly.
        return new TextContent(Encoding.UTF8.GetString(decoded));
    }

    /// <summary>
    /// Undoes every Content-Encoding token in <paramref name="contentEncodingHeader"/>,
    /// most-recently-applied first (RFC 9110 section 8.4). On failure,
    /// <paramref name="decoded"/> is <paramref name="body"/> unchanged and
    /// <paramref name="error"/> explains why -- a caller that only cares
    /// about the success path can ignore both and just check the return
    /// value.
    /// </summary>
    private static bool TryDecode(byte[] body, string? contentEncodingHeader, out byte[] decoded, out string? error)
    {
        decoded = body;
        error = null;
        if (string.IsNullOrWhiteSpace(contentEncodingHeader))
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
            // Deliberately broad: ZstdSharp, being a straight port of a C
            // library, doesn't document a closed set of exception types for
            // malformed input the way System.IO.Compression's
            // InvalidDataException does, and IInspector's contract is that
            // an inspector never throws for merely bad input -- it reports
            // an ErrorContent instead so one broken body can't take the
            // whole detail pane down. Most likely causes in practice: the
            // body claims an encoding it isn't actually in, or it's simply
            // not text at all (an image, say, with no Content-Encoding
            // header to explain it).
            output = input;
            error = $"Couldn't decompress the body as \"{token}\": {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// "deflate" is the one Content-Encoding whose own name doesn't say
    /// which framing it actually is: most servers send zlib-wrapped data
    /// (RFC 1950) under this name despite it, but some -- historically
    /// IIS -- send raw DEFLATE (RFC 1951) instead. Browsers cope by trying
    /// one and falling back to the other; this does the same, zlib first
    /// since it's the more common case in real traffic.
    /// </summary>
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

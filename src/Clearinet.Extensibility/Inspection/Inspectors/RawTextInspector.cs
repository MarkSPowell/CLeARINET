using Clearinet.ProxyCore.Http;

namespace Clearinet.Extensibility.Inspection.Inspectors;

/// <summary>
/// Fiddler Classic's "TextView", named "Raw" here since that's this first
/// cut's actual job: get from wire bytes to readable text, decompressed --
/// no pretty-printing or syntax highlighting yet (see <see cref="TextContent.SyntaxHint"/>
/// for where that would plug in later).
///
/// Decoding (every Content-Encoding seen on the modern web, and the
/// charset from Content-Type) lives in <see cref="ContentDecoder"/>, shared
/// with the JSON, WebForms and ImageView inspectors and with HAR export.
/// The notes below describe what it handles.
///
/// Handles every Content-Encoding actually seen on the modern web: gzip
/// (and its rare "x-gzip" alias), deflate, br (Brotli, via .NET's own
/// System.IO.Compression), and zstd (via ZstdSharp.Port, a pure managed
/// port -- .NET doesn't ship a built-in zstd decoder until .NET 11, per
/// Microsoft's own .NET 11 release notes, and this repo targets .NET 10; a
/// managed port avoids shipping a native binary per platform for the one
/// codec that gap leaves out). "deflate" is decoded as zlib-wrapped data
/// (RFC 1950) first, falling back to raw DEFLATE (RFC 1951) -- see
/// ContentDecoder's InflateDeflate for why both exist in the wild under this
/// one name. A Content-Encoding header can also list more than one coding
/// applied in sequence (RFC 9110 section 8.4, e.g. "gzip, br"); those are
/// undone in reverse, most-recently-applied first.
///
/// A few codings are recognized by name but never decoded -- see
/// ContentDecoder's KnownUnsupportedEncodings for which ones and why each is a
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

        if (!ContentDecoder.TryDecode(context.Body, context.FindHeader("Content-Encoding"), out var decoded, out var error))
        {
            return new ErrorContent(error!);
        }

        return new TextContent(ContentDecoder.DecodeText(decoded, context.FindHeader("Content-Type")));
    }
}

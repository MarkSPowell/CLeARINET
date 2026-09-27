using System.IO.Compression;

namespace Clearinet.Compatibility.FiddlerScript;

/// <summary>
/// Backs <see cref="Exchange.utilDecodeResponse"/>. Handles gzip, deflate
/// (zlib-wrapped first, falling back to raw DEFLATE, same reasoning as
/// <c>Clearinet.Extensibility.Inspection.Inspectors.RawTextInspector.InflateDeflate</c>),
/// and br (Brotli) -- all available from <c>System.IO.Compression</c> with
/// no extra package. Deliberately does NOT handle zstd the way
/// <c>RawTextInspector</c> does: that inspector pulls in <c>ZstdSharp.Port</c>,
/// and adding that dependency to <c>Clearinet.Compatibility</c> too, just
/// for this one codec, wasn't worth doing without asking first -- a script
/// calling <c>utilDecodeResponse()</c> against a zstd-encoded body gets an
/// honest <see cref="NotSupportedException"/> rather than silent garbage.
///
/// This is a deliberate duplication of <c>RawTextInspector</c>'s own
/// decode logic, not a shared call -- the two live in different layers
/// (<c>Clearinet.Extensibility</c> is UI-inspector-shaped, returning
/// <c>InspectorContent</c>; this needs to mutate an <see cref="Exchange"/>'s
/// body in place) and cross-referencing sideways between them felt like
/// the wrong coupling to introduce for four codec branches. Worth
/// extracting into a shared <c>Clearinet.ProxyCore</c> utility if the two
/// implementations ever need to drift back in sync.
/// </summary>
internal static class ExchangeCodec
{
    public static void DecodeInPlace(ExchangeResponse response)
    {
        var contentEncoding = response.headers["Content-Encoding"];
        if (string.IsNullOrWhiteSpace(contentEncoding))
        {
            return;
        }

        var tokens = contentEncoding.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var current = response.Body;
        for (var i = tokens.Length - 1; i >= 0; i--)
        {
            current = DecodeOne(current, tokens[i]);
        }

        response.Body = current;
        response.headers.Remove("Content-Encoding");
    }

    private static byte[] DecodeOne(byte[] input, string token) => token.ToLowerInvariant() switch
    {
        "identity" => input,
        "gzip" or "x-gzip" => ReadAllFrom(new GZipStream(new MemoryStream(input), CompressionMode.Decompress)),
        "deflate" => InflateDeflate(input),
        "br" => ReadAllFrom(new BrotliStream(new MemoryStream(input), CompressionMode.Decompress)),
        "zstd" => throw new NotSupportedException(
            "utilDecodeResponse() doesn't support zstd yet -- see ExchangeCodec's own remarks."),
        _ => input,
    };

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

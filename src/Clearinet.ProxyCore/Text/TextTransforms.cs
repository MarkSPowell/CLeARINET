using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clearinet.ProxyCore.Compare;

namespace Clearinet.ProxyCore.Text;

/// <summary>
/// What a transform produced. <see cref="Note"/> explains something about
/// the output (say, that decoded bytes weren't text, so they're shown as
/// hex); when <see cref="Failed"/>, <see cref="Output"/> is empty and the
/// note says why.
/// </summary>
public sealed record TextTransformResult(string Output, string? Note = null, bool Failed = false)
{
    public static TextTransformResult Error(string message) => new(string.Empty, message, Failed: true);
}

/// <summary>
/// One of TextWizard's conversions. <see cref="UsesEncoding"/> is true when
/// the chosen character encoding matters (text is turned into bytes, or
/// bytes back into text).
/// </summary>
public sealed record TextTransform(string Name, string Description, bool UsesEncoding, Func<string, Encoding, TextTransformResult> Apply)
{
    public override string ToString() => Name;
}

/// <summary>A character encoding TextWizard offers, by the name it shows.</summary>
public sealed record TextEncodingChoice(string Name, Encoding Encoding)
{
    public override string ToString() => Name;
}

/// <summary>
/// TextWizard (Tools > TextWizard, Ctrl+E), after Fiddler Classic's: the
/// encodings and decodings that come up while reading traffic. Base64, URL
/// and HTML encoding, hex, JavaScript string escapes, JWTs, JSON, Unix
/// times, hashes, and the deflate-then-Base64 that SAML redirects use.
/// </summary>
public static class TextTransforms
{
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The encodings to choose from; UTF-8 first, as the default.</summary>
    public static IReadOnlyList<TextEncodingChoice> Encodings { get; } =
    [
        new("UTF-8", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)),
        new("Latin-1 (ISO-8859-1)", Encoding.Latin1),
        new("UTF-16 LE", new UnicodeEncoding(bigEndian: false, byteOrderMark: false)),
    ];

    /// <summary>Every transform, in the order TextWizard lists them.</summary>
    public static IReadOnlyList<TextTransform> All { get; } =
    [
        new("To Base64", "Encodes the text's bytes as Base64.", true, ToBase64),
        new("From Base64", "Decodes Base64, including the URL-safe kind (- and _) and Base64 without its = padding.", true, FromBase64),
        new("URL encode", "Percent-encodes everything except letters, digits and - _ . ~", true, UrlEncode),
        new("URL decode", "Decodes %XX escapes, and + as a space.", true, UrlDecode),
        new("HTML encode", "Escapes < > & \" ' and non-ASCII characters as HTML entities.", false, (t, _) => new(WebUtility.HtmlEncode(t))),
        new("HTML decode", "Turns HTML entities (&amp; &#60; &#x3C;...) back into characters.", false, (t, _) => new(WebUtility.HtmlDecode(t))),
        new("To hex", "The text's bytes as hex.", true, ToHex),
        new("From hex", "Hex digits to bytes. Spaces, commas, colons, dashes, 0x, \\x and % between bytes are ignored.", true, FromHex),
        new("JavaScript escape", "Escapes the text for a JavaScript or JSON string: quotes, backslashes, control and non-ASCII characters.", false, (t, _) => new(JavaScriptEscape(t))),
        new("JavaScript unescape", "Turns \\n, \\\", \\xHH, \\uXXXX and \\u{...} escapes back into characters.", false, (t, _) => new(JavaScriptUnescape(t))),
        new("Decode JWT", "Shows a JSON Web Token's header and payload, with its times as dates. The signature isn't checked.", false, (t, _) => DecodeJwt(t)),
        new("Format JSON", "Indents JSON, one value per line.", false, (t, _) => FormatJson(t)),
        new("Unix time ↔ date", "A Unix time (seconds or milliseconds) as a date, or a date as a Unix time.", false, (t, _) => UnixTime(t)),
        new("Hashes", "MD5, SHA-1, SHA-256 and SHA-512 of the text's bytes.", true, Hashes),
        new("Deflate to Base64 (SAML)", "Compresses the text with raw deflate, then Base64, as SAML's HTTP-Redirect binding does.", true, DeflateToBase64),
        new("Inflate from Base64 (SAML)", "Base64 (URL-decoded first if needed), then decompressed: raw deflate, zlib or gzip.", true, InflateFromBase64),
    ];

    public static TextTransformResult ToBase64(string text, Encoding encoding) =>
        new(Convert.ToBase64String(encoding.GetBytes(text)));

    public static TextTransformResult FromBase64(string text, Encoding encoding) =>
        TryDecodeBase64(text, out var bytes, out var error) ? BytesToText(bytes, encoding) : TextTransformResult.Error(error);

    public static TextTransformResult UrlEncode(string text, Encoding encoding)
    {
        var result = new StringBuilder(text.Length * 3);
        foreach (var b in encoding.GetBytes(text))
        {
            if (b is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or
                (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~')
            {
                result.Append((char)b);
            }
            else
            {
                result.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return new TextTransformResult(result.ToString());
    }

    public static TextTransformResult UrlDecode(string text, Encoding encoding)
    {
        var bytes = new List<byte>(text.Length);
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length > 0)
            {
                bytes.AddRange(encoding.GetBytes(literal.ToString()));
                literal.Clear();
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '%' && i + 2 < text.Length && IsHexDigit(text[i + 1]) && IsHexDigit(text[i + 2]))
            {
                FlushLiteral();
                bytes.Add((byte)((HexValue(text[i + 1]) << 4) | HexValue(text[i + 2])));
                i += 2;
            }
            else if (c == '+')
            {
                FlushLiteral();
                bytes.Add((byte)' ');
            }
            else
            {
                literal.Append(c);
            }
        }

        FlushLiteral();
        return new TextTransformResult(encoding.GetString(bytes.ToArray()));
    }

    public static TextTransformResult ToHex(string text, Encoding encoding) =>
        new(Convert.ToHexString(encoding.GetBytes(text)) is { Length: > 0 } hex
            ? string.Join(' ', Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)))
            : string.Empty);

    public static TextTransformResult FromHex(string text, Encoding encoding) =>
        TryParseHex(text, out var bytes, out var error) ? BytesToText(bytes, encoding) : TextTransformResult.Error(error);

    /// <summary>
    /// Hex digits to bytes. Spaces, line breaks, commas, colons, dashes,
    /// semicolons, and <c>0x</c>, <c>\x</c> or <c>%</c> before a byte are
    /// ignored; anything else is an error.
    /// </summary>
    public static bool TryParseHex(string text, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;
        var digits = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (IsHexDigit(c))
            {
                // "0x" and "\x" prefixes: skip the x.
                if (c == '0' && i + 1 < text.Length && text[i + 1] is 'x' or 'X' && (digits.Length % 2 == 0))
                {
                    i++;
                    continue;
                }

                digits.Append(c);
            }
            else if (c == '\\' && i + 1 < text.Length && text[i + 1] is 'x' or 'X')
            {
                i++;
            }
            else if (!(char.IsWhiteSpace(c) || c is ',' or ':' or '-' or '%' or ';'))
            {
                error = $"\"{c}\" isn't a hex digit.";
                return false;
            }
        }

        if (digits.Length % 2 != 0)
        {
            error = "There's an odd number of hex digits, so the last byte is incomplete.";
            return false;
        }

        bytes = Convert.FromHexString(digits.ToString());
        return true;
    }

    /// <summary>Bytes as hex, <paramref name="perLine"/> to a line ("48 65 6C 6C 6F"), the form <see cref="TryParseHex"/> reads back.</summary>
    public static string FormatHex(byte[] bytes, int perLine = 16)
    {
        var hex = Convert.ToHexString(bytes);
        var text = new StringBuilder(bytes.Length * 3);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i > 0)
            {
                text.Append(i % perLine == 0 ? '\n' : ' ');
            }

            text.Append(hex, i * 2, 2);
        }

        return text.ToString();
    }

    public static string JavaScriptEscape(string text)
    {
        var result = new StringBuilder(text.Length + 16);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': result.Append(@"\\"); break;
                case '"': result.Append("\\\""); break;
                case '\'': result.Append("\\'"); break;
                case '\n': result.Append(@"\n"); break;
                case '\r': result.Append(@"\r"); break;
                case '\t': result.Append(@"\t"); break;
                case '\b': result.Append(@"\b"); break;
                case '\f': result.Append(@"\f"); break;
                default:
                    if (c < 0x20 || c > 0x7E)
                    {
                        result.Append(@"\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        result.Append(c);
                    }

                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>Unescapes JavaScript string escapes. A malformed escape is left as it is.</summary>
    public static string JavaScriptUnescape(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\\' || i + 1 >= text.Length)
            {
                result.Append(c);
                continue;
            }

            var next = text[i + 1];
            switch (next)
            {
                case 'n': result.Append('\n'); i++; break;
                case 'r': result.Append('\r'); i++; break;
                case 't': result.Append('\t'); i++; break;
                case 'b': result.Append('\b'); i++; break;
                case 'f': result.Append('\f'); i++; break;
                case 'v': result.Append('\v'); i++; break;
                case '0' when i + 2 >= text.Length || !char.IsAsciiDigit(text[i + 2]): result.Append('\0'); i++; break;
                case 'x' when i + 3 < text.Length && IsHexDigit(text[i + 2]) && IsHexDigit(text[i + 3]):
                    result.Append((char)((HexValue(text[i + 2]) << 4) | HexValue(text[i + 3])));
                    i += 3;
                    break;
                case 'u' when i + 2 < text.Length && text[i + 2] == '{':
                {
                    var close = text.IndexOf('}', i + 3);
                    if (close > i + 3 && close - (i + 3) <= 6 &&
                        int.TryParse(text.AsSpan(i + 3, close - (i + 3)), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint) &&
                        codePoint <= 0x10FFFF && codePoint is < 0xD800 or > 0xDFFF)
                    {
                        result.Append(char.ConvertFromUtf32(codePoint));
                        i = close;
                    }
                    else
                    {
                        result.Append(c);
                    }

                    break;
                }

                case 'u' when i + 5 < text.Length &&
                              int.TryParse(text.AsSpan(i + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unit):
                    result.Append((char)unit);
                    i += 5;
                    break;
                case 'u' or 'x':
                    result.Append(c); // malformed: keep the backslash
                    break;
                default:
                    // \" \' \\ \/ and any other character: the character itself.
                    result.Append(next);
                    i++;
                    break;
            }
        }

        return result.ToString();
    }

    public static TextTransformResult DecodeJwt(string text)
    {
        var token = text.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = token[7..].Trim();
        }

        var parts = token.Split('.');
        if (parts.Length == 5)
        {
            return TextTransformResult.Error("This has five parts, so it's an encrypted JWT (JWE); its payload can't be read without the key.");
        }

        if (parts.Length is not (2 or 3))
        {
            return TextTransformResult.Error("A JWT is two or three Base64URL parts separated by dots (header.payload.signature).");
        }

        var output = new StringBuilder();
        JsonNode? payload = null;
        for (var p = 0; p < 2; p++)
        {
            var label = p == 0 ? "Header" : "Payload";
            if (!TryDecodeBase64(parts[p], out var bytes, out var error))
            {
                return TextTransformResult.Error($"The {label.ToLowerInvariant()} isn't Base64URL: {error}");
            }

            var json = Encoding.UTF8.GetString(bytes);
            output.Append(label).Append(":\n");
            try
            {
                var node = JsonNode.Parse(json);
                payload = p == 1 ? node : payload;
                output.Append(node?.ToJsonString(IndentedJson) ?? "null");
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
            {
                output.Append(json);
            }

            output.Append("\n\n");
        }

        if (payload is JsonObject claims)
        {
            var times = new List<string>();
            foreach (var (claim, meaning) in new[] { ("iat", "issued"), ("nbf", "not before"), ("exp", "expires") })
            {
                if (claims[claim] is JsonValue value && value.TryGetValue<long>(out var seconds) &&
                    seconds is >= -62135596800 and <= 253402300799)
                {
                    times.Add($"{claim} ({meaning}): {DateTimeOffset.FromUnixTimeSeconds(seconds):yyyy-MM-dd HH:mm:ss} UTC");
                }
            }

            if (times.Count > 0)
            {
                output.Append(string.Join('\n', times)).Append("\n\n");
            }
        }

        var signature = parts.Length > 2 && TryDecodeBase64(parts[2], out var signatureBytes, out _) ? signatureBytes.Length : 0;
        output.Append(signature > 0 ? $"Signature: {signature} bytes" : "No signature");
        return new TextTransformResult(output.ToString(), "The signature isn't checked.");
    }

    public static TextTransformResult FormatJson(string text)
    {
        try
        {
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return new TextTransformResult(node?.ToJsonString(IndentedJson) ?? "null");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return TextTransformResult.Error($"That isn't valid JSON: {ex.Message}");
        }
    }

    public static TextTransformResult UnixTime(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return new TextTransformResult(string.Empty);
        }

        if (long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            // Beyond the year 5000 in seconds, it's surely milliseconds.
            var milliseconds = Math.Abs(number) >= 100_000_000_000L;
            try
            {
                var date = milliseconds ? DateTimeOffset.FromUnixTimeMilliseconds(number) : DateTimeOffset.FromUnixTimeSeconds(number);
                return new TextTransformResult(
                    $"UTC:   {date:yyyy-MM-dd HH:mm:ss.fff}\nLocal: {date.ToLocalTime():yyyy-MM-dd HH:mm:ss.fff zzz}",
                    milliseconds ? "Read as milliseconds." : "Read as seconds.");
            }
            catch (ArgumentOutOfRangeException)
            {
                return TextTransformResult.Error("That's out of range for a date.");
            }
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ||
            DateTimeOffset.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.AssumeUniversal, out parsed))
        {
            return new TextTransformResult(
                $"Seconds:      {parsed.ToUnixTimeSeconds()}\nMilliseconds: {parsed.ToUnixTimeMilliseconds()}",
                "A date with no time zone is read as UTC.");
        }

        return TextTransformResult.Error("Type a Unix time (a number) or a date.");
    }

    public static TextTransformResult Hashes(string text, Encoding encoding)
    {
        var bytes = encoding.GetBytes(text);
        return new TextTransformResult(
            $"MD5:     {Hex(MD5.HashData(bytes))}\n" +
            $"SHA-1:   {Hex(SHA1.HashData(bytes))}\n" +
            $"SHA-256: {Hex(SHA256.HashData(bytes))}\n" +
            $"SHA-512: {Hex(SHA512.HashData(bytes))}\n" +
            $"SHA-256 (Base64): {Convert.ToBase64String(SHA256.HashData(bytes))}");

        static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static TextTransformResult DeflateToBase64(string text, Encoding encoding)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(encoding.GetBytes(text));
        }

        return new TextTransformResult(Convert.ToBase64String(output.ToArray()));
    }

    public static TextTransformResult InflateFromBase64(string text, Encoding encoding)
    {
        var input = text.Trim();
        if (input.Contains('%', StringComparison.Ordinal))
        {
            input = Uri.UnescapeDataString(input); // copied from a URL's SAMLRequest= parameter
        }

        if (input.Length == 0)
        {
            return new TextTransformResult(string.Empty);
        }

        if (!TryDecodeBase64(input, out var compressed, out var error))
        {
            return TextTransformResult.Error(error);
        }

        (string Name, Func<Stream, Stream> Open) rawDeflate = ("raw deflate", s => new DeflateStream(s, CompressionMode.Decompress));
        (string Name, Func<Stream, Stream> Open) zlib = ("zlib", s => new ZLibStream(s, CompressionMode.Decompress));
        (string Name, Func<Stream, Stream> Open) gzip = ("gzip", s => new GZipStream(s, CompressionMode.Decompress));

        // SAML uses raw deflate; try first whatever the first bytes suggest.
        var formats = compressed is [0x1F, 0x8B, ..] ? new[] { gzip, rawDeflate, zlib }
            : compressed is [var b0, var b1, ..] && (b0 & 0x0F) == 8 && ((b0 << 8) | b1) % 31 == 0 ? new[] { zlib, rawDeflate, gzip }
            : new[] { rawDeflate, zlib, gzip };

        foreach (var (name, open) in formats)
        {
            try
            {
                using var source = open(new MemoryStream(compressed));
                using var inflated = new MemoryStream();
                source.CopyTo(inflated);
                var result = BytesToText(inflated.ToArray(), encoding);
                return result with { Note = string.Join(' ', new[] { $"Decompressed as {name}.", result.Note }.Where(n => !string.IsNullOrEmpty(n))) };
            }
            catch (InvalidDataException)
            {
                // try the next format
            }
        }

        return TextTransformResult.Error("The Base64 decodes, but not to deflate, zlib or gzip data.");
    }

    /// <summary>
    /// Base64 to bytes, accepting the URL-safe alphabet, missing padding and
    /// whitespace (line breaks in pasted Base64).
    /// </summary>
    public static bool TryDecodeBase64(string text, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;
        var clean = new StringBuilder(text.Length + 3);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || c == '=')
            {
                continue;
            }

            clean.Append(c switch { '-' => '+', '_' => '/', _ => c });
        }

        if (clean.Length % 4 == 1)
        {
            error = "Base64 can't be that length; part of it is missing.";
            return false;
        }

        clean.Append('=', (4 - (clean.Length % 4)) % 4);
        var buffer = new byte[clean.Length * 3 / 4];
        if (!Convert.TryFromBase64String(clean.ToString(), buffer, out var written))
        {
            var bad = clean.ToString().FirstOrDefault(c => !(char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '='));
            error = bad == default ? "That isn't valid Base64." : $"\"{bad}\" isn't a Base64 character.";
            return false;
        }

        bytes = buffer[..written];
        return true;
    }

    /// <summary>
    /// Decoded bytes as text in <paramref name="encoding"/>, or, when they
    /// aren't text in it (invalid sequences, or control characters other than
    /// tabs and line breaks), as a hex dump with a note saying so.
    /// </summary>
    public static TextTransformResult BytesToText(byte[] bytes, Encoding encoding)
    {
        var strict = (Encoding)encoding.Clone();
        strict.DecoderFallback = DecoderFallback.ExceptionFallback;
        try
        {
            var text = strict.GetString(bytes);
            if (!text.Any(c => char.IsControl(c) && c is not ('\t' or '\n' or '\r')))
            {
                return new TextTransformResult(text);
            }
        }
        catch (DecoderFallbackException)
        {
            // not text in this encoding
        }

        return new TextTransformResult(
            SessionComparison.HexDump(bytes),
            $"The result ({bytes.Length:N0} bytes) isn't {encoding.WebName.ToUpperInvariant()} text, so it's shown as hex.");
    }

    private static bool IsHexDigit(char c) => char.IsAsciiHexDigit(c);

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => c - 'A' + 10,
    };
}

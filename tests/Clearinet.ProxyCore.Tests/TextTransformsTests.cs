using System.Text;
using Clearinet.ProxyCore.Text;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

/// <summary>TextWizard's conversions (<see cref="TextTransforms"/>).</summary>
public class TextTransformsTests
{
    private static readonly Encoding Utf8 = TextTransforms.Encodings[0].Encoding;

    [Fact]
    public void Base64RoundTripsNonAsciiText()
    {
        Assert.Equal("aMOpbGxvIHfDtnJsZA==", TextTransforms.ToBase64("héllo wörld", Utf8).Output);
        Assert.Equal("héllo wörld", TextTransforms.FromBase64("aMOpbGxvIHfDtnJsZA==", Utf8).Output);
    }

    [Theory]
    [InlineData("aMOpbGxvIHfDtnJsZA")] // no padding
    [InlineData("aMOp bGxv\r\nIHfD tnJsZA==")] // pasted with line breaks
    public void FromBase64IsForgiving(string input)
    {
        Assert.Equal("héllo wörld", TextTransforms.FromBase64(input, Utf8).Output);
    }

    [Fact]
    public void FromBase64AcceptsTheUrlSafeAlphabet()
    {
        // 0xFB 0xFF is "+/8=" in standard Base64, "-_8" URL-safe; "ûÿ" in Latin-1.
        Assert.Equal("ûÿ", TextTransforms.FromBase64("-_8", Encoding.Latin1).Output);
    }

    [Fact]
    public void FromBase64ShowsBinaryAsHex()
    {
        var result = TextTransforms.FromBase64("AAECAw==", Utf8);

        Assert.False(result.Failed);
        Assert.StartsWith("00000000  00 01 02 03", result.Output);
        Assert.Contains("shown as hex", result.Note);
    }

    [Fact]
    public void FromBase64ReportsBadInput()
    {
        var result = TextTransforms.FromBase64("abc$", Utf8);

        Assert.True(result.Failed);
        Assert.Contains("\"$\"", result.Note);
    }

    [Fact]
    public void UrlEncodingRoundTrips()
    {
        var encoded = TextTransforms.UrlEncode("a b&c=é/~", Utf8).Output;

        Assert.Equal("a%20b%26c%3D%C3%A9%2F~", encoded);
        Assert.Equal("a b&c=é/~", TextTransforms.UrlDecode(encoded, Utf8).Output);
    }

    [Fact]
    public void UrlDecodeTreatsPlusAsSpaceAndKeepsStrayPercents()
    {
        Assert.Equal("a b 100% é", TextTransforms.UrlDecode("a+b%20100% é", Utf8).Output);
    }

    [Fact]
    public void UrlEncodingFollowsTheChosenEncoding()
    {
        Assert.Equal("%E9", TextTransforms.UrlEncode("é", Encoding.Latin1).Output);
        Assert.Equal("é", TextTransforms.UrlDecode("%E9", Encoding.Latin1).Output);
    }

    [Fact]
    public void HexRoundTrips()
    {
        Assert.Equal("48 69 C3 A9", TextTransforms.ToHex("Hié", Utf8).Output);
        Assert.Equal("Hié", TextTransforms.FromHex("48 69 C3 A9", Utf8).Output);
    }

    [Theory]
    [InlineData("4869")]
    [InlineData("0x48, 0x69")]
    [InlineData("\\x48\\x69")]
    [InlineData("%48%69")]
    [InlineData("48:69")]
    public void FromHexIgnoresSeparatorsAndPrefixes(string input)
    {
        Assert.Equal("Hi", TextTransforms.FromHex(input, Utf8).Output);
    }

    [Fact]
    public void FromHexReportsBadInput()
    {
        Assert.True(TextTransforms.FromHex("4G", Utf8).Failed);
        Assert.True(TextTransforms.FromHex("486", Utf8).Failed);
    }

    [Fact]
    public void JavaScriptEscapingRoundTrips()
    {
        var original = "Say \"hi\"\n\ttab \\ é 😀";

        var escaped = TextTransforms.JavaScriptEscape(original);

        Assert.Equal("Say \\\"hi\\\"\\n\\ttab \\\\ \\u00E9 \\uD83D\\uDE00", escaped);
        Assert.Equal(original, TextTransforms.JavaScriptUnescape(escaped));
    }

    [Fact]
    public void JavaScriptUnescapeHandlesEveryForm()
    {
        Assert.Equal("A A 😀 / ' \\x", TextTransforms.JavaScriptUnescape("\\x41 \\u0041 \\u{1F600} \\/ \\' \\x"));
        Assert.Equal("\\u12", TextTransforms.JavaScriptUnescape("\\u12"));
    }

    [Fact]
    public void HtmlRoundTrips()
    {
        var html = TextTransforms.All.Single(t => t.Name == "HTML encode").Apply("<a href=\"x\">&</a>", Utf8).Output;

        Assert.Equal("&lt;a href=&quot;x&quot;&gt;&amp;&lt;/a&gt;", html);
        Assert.Equal("<a href=\"x\">&</a>", TextTransforms.All.Single(t => t.Name == "HTML decode").Apply(html, Utf8).Output);
    }

    [Fact]
    public void DecodeJwtShowsHeaderPayloadAndTimes()
    {
        var result = TextTransforms.DecodeJwt(
            "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjMiLCJuYW1lIjoiQW5uIiwiZXhwIjoxNzkyMTYwMDAwfQ.c2lnbmF0dXJl");

        Assert.False(result.Failed);
        Assert.Contains("\"alg\": \"HS256\"", result.Output);
        Assert.Contains("\"name\": \"Ann\"", result.Output);
        Assert.Contains("exp (expires): 2026-10-16 14:13:20 UTC", result.Output);
        Assert.Contains("Signature: 9 bytes", result.Output);
        Assert.Equal("The signature isn't checked.", result.Note);
    }

    [Theory]
    [InlineData("not a token")]
    [InlineData("a.b.c.d.e")]
    public void DecodeJwtReportsWhatItCantRead(string input)
    {
        Assert.True(TextTransforms.DecodeJwt(input).Failed);
    }

    [Fact]
    public void FormatJsonIndents()
    {
        Assert.Equal("{\n  \"a\": [\n    1,\n    2\n  ]\n}", TextTransforms.FormatJson("{\"a\":[1,2]}").Output.Replace("\r\n", "\n"));
        Assert.True(TextTransforms.FormatJson("{oops").Failed);
    }

    [Fact]
    public void UnixTimeReadsSecondsAndMilliseconds()
    {
        Assert.StartsWith("UTC:   2023-11-14 22:13:20.000", TextTransforms.UnixTime("1700000000").Output);
        Assert.StartsWith("UTC:   2023-11-14 22:13:20.123", TextTransforms.UnixTime("1700000000123").Output);
        Assert.Equal("Read as milliseconds.", TextTransforms.UnixTime("1700000000123").Note);
    }

    [Fact]
    public void UnixTimeTurnsADateIntoSeconds()
    {
        var result = TextTransforms.UnixTime("2023-11-14T22:13:20Z");

        Assert.Equal("Seconds:      1700000000\nMilliseconds: 1700000000000", result.Output);
    }

    [Fact]
    public void HashesOfAbc()
    {
        var output = TextTransforms.Hashes("abc", Utf8).Output;

        Assert.Contains("MD5:     900150983cd24fb0d6963f7d28e17f72", output);
        Assert.Contains("SHA-256: ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", output);
    }

    [Fact]
    public void SamlDeflateRoundTrips()
    {
        const string saml = "<samlp:AuthnRequest ID=\"x\"/>";

        var deflated = TextTransforms.DeflateToBase64(saml, Utf8).Output;

        Assert.Equal(saml, TextTransforms.InflateFromBase64(deflated, Utf8).Output);
    }

    [Fact]
    public void InflateReadsAUrlEncodedSamlRequest()
    {
        // As copied from ?SAMLRequest=... (deflated by another library).
        var result = TextTransforms.InflateFromBase64("sylOzM0psHIsLcnIC0otLE0tLlHwdLFVqlDStwMA", Utf8);

        Assert.Equal("<samlp:AuthnRequest ID=\"x\"/>", result.Output);
        Assert.Equal("Decompressed as raw deflate.", result.Note);
        Assert.Equal(result.Output, TextTransforms.InflateFromBase64("sylOzM0psHIsLcnIC0otLE0tLlHwdLFVqlDStwMA".Replace("/", "%2F"), Utf8).Output);
    }

    [Fact]
    public void EveryTransformHandlesEmptyInput()
    {
        foreach (var transform in TextTransforms.All)
        {
            // Doesn't throw; anything it does produce is empty or an explanation.
            var result = transform.Apply(string.Empty, Utf8);
            Assert.NotNull(result.Output);
        }
    }
}

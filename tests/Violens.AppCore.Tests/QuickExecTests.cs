using System.Text;
using Violens.ProxyCore.Http;
using Violens.AppCore.QuickExec;
using Violens.ProxyCore.Sessions;
using Violens.AppCore.Text;
using Xunit;

namespace Violens.AppCore.Tests;

/// <summary>QuickExec commands (<see cref="QuickExecParser"/>), and the Composer's exact-bytes body helpers.</summary>
public class QuickExecTests
{
    private static Session Exchange(
        string method = "GET",
        string target = "/api/items",
        string host = "api.example.test",
        int status = 200,
        string contentType = "application/json",
        int bodyLength = 10)
    {
        var request = new CapturedRequest(method, target, "HTTP/1.1", [("Host", host)], []);
        var response = new CapturedResponse("HTTP/1.1", status, "OK", [("Content-Type", contentType)], new byte[bodyLength]);
        return new SessionStore().Add(host, DateTimeOffset.UtcNow, request, response);
    }

    private static QuickExecCommand Parse(string line) => Assert.IsType<QuickExecCommand>(QuickExecParser.Parse(line));

    [Fact]
    public void BlankLinesAreNothing()
    {
        Assert.Null(QuickExecParser.Parse("   "));
    }

    [Theory]
    [InlineData("?items", QuickExecKind.SelectUrl, "items")]
    [InlineData("@example", QuickExecKind.SelectHost, "example")]
    [InlineData("=404", QuickExecKind.SelectStatus, "404")]
    [InlineData("=post", QuickExecKind.SelectMethod, "post")]
    [InlineData("select image", QuickExecKind.SelectContentType, "image")]
    [InlineData("allbut json", QuickExecKind.KeepOnlyContentType, "json")]
    [InlineData("KeepOnly json", QuickExecKind.KeepOnlyContentType, "json")]
    [InlineData("cls", QuickExecKind.Clear, "")]
    [InlineData("clear", QuickExecKind.Clear, "")]
    [InlineData("bpu /login", QuickExecKind.BreakOnRequestUrl, "/login")]
    [InlineData("bpu", QuickExecKind.BreakOnRequestUrl, "")]
    [InlineData("bpafter \"my api\"", QuickExecKind.BreakOnResponseUrl, "my api")]
    [InlineData("bpm post", QuickExecKind.BreakOnMethod, "POST")]
    [InlineData("bpv PUT", QuickExecKind.BreakOnMethod, "PUT")]
    [InlineData("go", QuickExecKind.ResumeAll, "")]
    [InlineData("start", QuickExecKind.Start, "")]
    [InlineData("stop", QuickExecKind.Stop, "")]
    [InlineData("help", QuickExecKind.Help, "")]
    [InlineData("mycommand a b", QuickExecKind.Other, "a b")]
    public void ParsesCommands(string line, QuickExecKind kind, string argument)
    {
        var command = Parse(line);

        Assert.Equal(kind, command.Kind);
        Assert.Equal(argument, command.Argument);
    }

    [Fact]
    public void ParsesNumbers()
    {
        Assert.Equal(404, Parse("=404").Number);
        Assert.Equal(500, Parse("bps 500").Number);
        Assert.Equal(100, Parse("tail 100").Number);
        Assert.Equal(QuickExecKind.Other, Parse("tail lots").Kind);
        Assert.Equal(40 * 1024, Parse(">40k").Number);
        Assert.Equal(1024, Parse("<1kb").Number);
    }

    [Theory]
    [InlineData("1000", 1000)]
    [InlineData("40k", 40 * 1024)]
    [InlineData("40KB", 40 * 1024)]
    [InlineData("1.5mb", 1572864)]
    [InlineData("12b", 12)]
    public void ParsesSizes(string text, long bytes)
    {
        Assert.True(QuickExecParser.TryParseSize(text, out var parsed));
        Assert.Equal(bytes, parsed);
    }

    [Fact]
    public void SplitsWordsKeepingQuotedTextTogether()
    {
        Assert.Equal(new[] { "prefs", "set", "a.b", "two words", "" }, QuickExecParser.SplitArguments("prefs set a.b \"two words\" \"\""));
    }

    [Fact]
    public void SelectionsMatchTheRightSessions()
    {
        var json = Exchange();
        var image = Exchange(method: "POST", target: "/img/logo.png", host: "cdn.example.test", status: 404, contentType: "image/png", bodyLength: 50_000);

        Assert.True(QuickExecParser.Matches(Parse("?ITEMS"), json));
        Assert.False(QuickExecParser.Matches(Parse("?items"), image));
        Assert.True(QuickExecParser.Matches(Parse("@cdn"), image));
        Assert.True(QuickExecParser.Matches(Parse("=404"), image));
        Assert.True(QuickExecParser.Matches(Parse("=post"), image));
        Assert.False(QuickExecParser.Matches(Parse("=post"), json));
        Assert.True(QuickExecParser.Matches(Parse(">40k"), image));
        Assert.False(QuickExecParser.Matches(Parse(">40k"), json));
        Assert.True(QuickExecParser.Matches(Parse("<1k"), json));
        Assert.True(QuickExecParser.Matches(Parse("select image"), image));
        Assert.True(QuickExecParser.Matches(Parse("allbut json"), json));
        Assert.False(QuickExecParser.Matches(Parse("allbut json"), image));
    }

    [Fact]
    public void SelectionCommandsAreMarkedAsSuch()
    {
        Assert.True(Parse("?x").IsSelection);
        Assert.True(Parse("select x").IsSelection);
        Assert.False(Parse("allbut x").IsSelection);
        Assert.False(Parse("cls").IsSelection);
    }

    [Fact]
    public void HexRoundTripsEveryByte()
    {
        var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        var hex = TextTransforms.FormatHex(bytes);

        Assert.StartsWith("00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F\n10 11", hex);
        Assert.True(TextTransforms.TryParseHex(hex, out var parsed, out _));
        Assert.Equal(bytes, parsed);
    }

    [Fact]
    public void HexParsingReportsMistakes()
    {
        Assert.False(TextTransforms.TryParseHex("89 50 4G", out _, out var error));
        Assert.Contains("\"G\"", error);
        Assert.False(TextTransforms.TryParseHex("89 5", out _, out _));
    }

    [Fact]
    public void FormatHeadShowsTheRequestWithoutItsBody()
    {
        var request = new CapturedRequest(
            "POST",
            "https://upload.example.test/files",
            "HTTP/1.1",
            [("Host", "upload.example.test"), ("Content-Type", "application/octet-stream")],
            [0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF]);

        var head = HttpMessageText.FormatHead(request);

        Assert.StartsWith("POST https://upload.example.test/files HTTP/1.1\r\n", head);
        Assert.Contains("Content-Length: 6\r\n", head);
        Assert.EndsWith("\r\n\r\n", head);
        Assert.DoesNotContain("PNG", head);

        // Parsed back with the hex body, it's the same request.
        Assert.True(HttpMessageText.TryParseRequest(head, out var parsed, out _));
        Assert.True(TextTransforms.TryParseHex(TextTransforms.FormatHex(request.Body), out var body, out _));
        Assert.Equal(request.Body, (parsed! with { Body = body }).Body);
        Assert.Equal("POST", parsed.Method);
    }

    [Fact]
    public void BinaryBodiesAreRecognisedAsNotText()
    {
        Assert.NotNull(TextTransforms.BytesToText([0x89, 0x50, 0x4E, 0x47, 0x00, 0xFF], new UTF8Encoding(false)).Note);
        Assert.Null(TextTransforms.BytesToText(Encoding.UTF8.GetBytes("{\"a\": 1}\n"), new UTF8Encoding(false)).Note);
    }
}

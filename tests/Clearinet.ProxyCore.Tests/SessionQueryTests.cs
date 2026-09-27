using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.ProxyCore.Tests;

public class SessionQueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_OfEmptyOrWhitespaceText_ReturnsMatchAll(string? text)
    {
        var query = SessionQuery.Parse(text);

        Assert.True(query.IsEmpty);
        Assert.Same(SessionQuery.MatchAll, query);
        Assert.True(query.Matches(SampleSession()));
    }

    [Fact]
    public void FreeText_MatchesTheUrlCaseInsensitively()
    {
        var session = SampleSession(host: "example.test", target: "/api/Widgets");

        Assert.True(SessionQuery.Parse("widgets").Matches(session));
        Assert.True(SessionQuery.Parse("EXAMPLE.TEST").Matches(session));
        Assert.False(SessionQuery.Parse("nope").Matches(session));
    }

    [Fact]
    public void FreeText_MatchesARequestHeaderNameOrValue()
    {
        var session = SampleSession(requestHeaders: [("X-Trace-Id", "abc-123")]);

        Assert.True(SessionQuery.Parse("x-trace-id").Matches(session));
        Assert.True(SessionQuery.Parse("abc-123").Matches(session));
    }

    [Fact]
    public void FreeText_MatchesAResponseHeaderNameOrValue()
    {
        var session = SampleSession(responseHeaders: [("Content-Type", "application/json")]);

        Assert.True(SessionQuery.Parse("application/json").Matches(session));
    }

    [Fact]
    public void FreeText_NeverMatchesBodyContent()
    {
        // Deliberate: bodies can be large and are whatever encoding the
        // wire actually used, not decoded text -- see SessionQuery's own
        // remarks. A phrase that appears only in a body must not match.
        var session = SampleSession(requestBody: "this-only-lives-in-the-body"u8.ToArray());

        Assert.False(SessionQuery.Parse("this-only-lives-in-the-body").Matches(session));
    }

    [Fact]
    public void Method_MatchesExactlyAndCaseInsensitively()
    {
        var session = SampleSession(method: "POST");

        Assert.True(SessionQuery.Parse("method:POST").Matches(session));
        Assert.True(SessionQuery.Parse("method:post").Matches(session));
        Assert.False(SessionQuery.Parse("method:GET").Matches(session));
    }

    [Fact]
    public void Method_WithACommaSeparatedListMatchesAnyOfThem()
    {
        var session = SampleSession(method: "PUT");

        Assert.True(SessionQuery.Parse("method:GET,PUT,DELETE").Matches(session));
        Assert.False(SessionQuery.Parse("method:GET,DELETE").Matches(session));
    }

    [Fact]
    public void Host_MatchesAsASubstringCaseInsensitively()
    {
        var session = SampleSession(host: "api.example.test");

        Assert.True(SessionQuery.Parse("host:example").Matches(session));
        Assert.True(SessionQuery.Parse("host:API.EXAMPLE").Matches(session));
        Assert.False(SessionQuery.Parse("host:other.test").Matches(session));
    }

    [Theory]
    [InlineData("status:200", 200, true)]
    [InlineData("status:200", 404, false)]
    [InlineData("status:4xx", 404, true)]
    [InlineData("status:4XX", 499, true)]
    [InlineData("status:4xx", 500, false)]
    [InlineData("status:>=400", 400, true)]
    [InlineData("status:>=400", 399, false)]
    [InlineData("status:<=499", 499, true)]
    [InlineData("status:<=499", 500, false)]
    [InlineData("status:>400", 401, true)]
    [InlineData("status:>400", 400, false)]
    [InlineData("status:<500", 499, true)]
    [InlineData("status:<500", 500, false)]
    [InlineData("status:400-499", 450, true)]
    [InlineData("status:400-499", 399, false)]
    [InlineData("status:400-499", 500, false)]
    public void Status_SupportsExactClassComparisonAndRangeForms(string query, int statusCode, bool expectedMatch)
    {
        var session = SampleSession(statusCode: statusCode);

        Assert.Equal(expectedMatch, SessionQuery.Parse(query).Matches(session));
    }

    [Fact]
    public void Status_WithAnUnparseableValueMatchesNothingRatherThanEverything()
    {
        var session = SampleSession(statusCode: 200);

        Assert.False(SessionQuery.Parse("status:banana").Matches(session));
    }

    [Fact]
    public void MultipleTokens_AreCombinedWithAnd()
    {
        var session = SampleSession(method: "POST", host: "example.test", statusCode: 500);

        Assert.True(SessionQuery.Parse("method:POST host:example status:5xx").Matches(session));
        Assert.False(SessionQuery.Parse("method:POST host:example status:2xx").Matches(session));
    }

    [Fact]
    public void QuotedText_KeepsWhitespaceInsideOneToken()
    {
        var session = SampleSession(requestHeaders: [("User-Agent", "My Custom Agent/1.0")]);

        // Without quoting, "custom agent" would be two separate AND'd
        // tokens; both happen to still match here, so the real assertion is
        // the phrase-preserving one below.
        Assert.True(SessionQuery.Parse("\"custom agent\"").Matches(session));
        Assert.False(SessionQuery.Parse("\"custom banana\"").Matches(session));
    }

    [Fact]
    public void AColonAtTheVeryStartOrEndOfATokenIsTreatedAsPlainFreeText()
    {
        var session = SampleSession(target: "/path:weird", requestHeaders: [("X-Trailing", "value:")]);

        Assert.True(SessionQuery.Parse(":weird").Matches(session));
        Assert.True(SessionQuery.Parse("value:").Matches(session));
    }

    [Fact]
    public void AnUnrecognizedKeyIsTreatedAsPlainFreeTextIncludingItsColon()
    {
        var session = SampleSession(requestHeaders: [("X-Note", "scheme:http noted here")]);

        Assert.True(SessionQuery.Parse("scheme:http").Matches(session));
    }

    private static Session SampleSession(
        string host = "example.test",
        string method = "GET",
        string target = "/",
        int statusCode = 200,
        IReadOnlyList<(string Name, string Value)>? requestHeaders = null,
        IReadOnlyList<(string Name, string Value)>? responseHeaders = null,
        byte[]? requestBody = null)
    {
        var request = new CapturedRequest(method, target, "HTTP/1.1", requestHeaders ?? [], requestBody ?? []);
        var response = new CapturedResponse("HTTP/1.1", statusCode, "Status", responseHeaders ?? [], []);
        return new Session(1, host, DateTimeOffset.UtcNow, request, response);
    }
}

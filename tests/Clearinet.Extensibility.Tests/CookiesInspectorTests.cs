using Clearinet.Extensibility.Inspection;
using Clearinet.Extensibility.Inspection.Inspectors;
using Clearinet.ProxyCore;
using Clearinet.ProxyCore.Http;
using Clearinet.ProxyCore.Sessions;
using Xunit;

namespace Clearinet.Extensibility.Tests;

/// <summary>The built-in Cookies tab (RFC 6265 parsing, plus the checks browsers enforce).</summary>
public class CookiesInspectorTests
{
    private static Session SessionWith(
        IReadOnlyList<(string Name, string Value)> requestHeaders,
        IReadOnlyList<(string Name, string Value)> responseHeaders) =>
        new(
            1,
            "example.test",
            DateTimeOffset.UnixEpoch,
            new CapturedRequest("GET", "/", "HTTP/1.1", requestHeaders, []),
            new CapturedResponse("HTTP/1.1", 200, "OK", responseHeaders, []),
            SessionState.Done);

    private static IReadOnlyList<HeaderRow> Rows(Session session, InspectorSide side) =>
        Assert.IsType<KeyValueContent>(new CookiesInspector().Inspect(new InspectorContext(session, side))).Rows;

    [Fact]
    public void AppearsOnlyOnASideWithCookies()
    {
        var inspector = new CookiesInspector();
        var requestOnly = SessionWith([("Cookie", "a=1")], []);
        var responseOnly = SessionWith([], [("set-cookie", "a=1")]);

        Assert.True(inspector.CanInspect(new InspectorContext(requestOnly, InspectorSide.Request)));
        Assert.False(inspector.CanInspect(new InspectorContext(requestOnly, InspectorSide.Response)));
        Assert.False(inspector.CanInspect(new InspectorContext(responseOnly, InspectorSide.Request)));
        Assert.True(inspector.CanInspect(new InspectorContext(responseOnly, InspectorSide.Response)));
    }

    [Fact]
    public void SplitsTheCookieHeaderIntoOneRowPerCookie()
    {
        var session = SessionWith([("Cookie", "session=abc123; theme=dark;  lonely"), ("Cookie", "b=x=y")], []);

        var rows = Rows(session, InspectorSide.Request);

        Assert.Equal(
            new[] { ("session", "abc123"), ("theme", "dark"), ("(no name)", "lonely"), ("b", "x=y") },
            rows.Select(r => (r.Name, r.Value)).ToArray());
    }

    [Fact]
    public void ShowsEachSetCookieWithItsAttributes()
    {
        var session = SessionWith([], [
            ("Set-Cookie", "id=a3fWa; Expires=Wed, 21 Oct 2026 07:28:00 GMT; Secure; HttpOnly"),
            ("Set-Cookie", "lang=en"),
        ]);

        var rows = Rows(session, InspectorSide.Response);

        Assert.Equal(2, rows.Count);
        Assert.Equal("id", rows[0].Name);
        Assert.Equal("a3fWa   [Expires=Wed, 21 Oct 2026 07:28:00 GMT; Secure; HttpOnly]", rows[0].Value);
        Assert.Equal(("lang", "en"), (rows[1].Name, rows[1].Value));
    }

    [Theory]
    [InlineData("a=1; SameSite=None", "SameSite=None without Secure")]
    [InlineData("__Secure-a=1; Path=/", "__Secure- prefix requires Secure")]
    [InlineData("__Host-a=1; Secure; Path=/; Domain=example.test", "__Host- prefix requires Secure, Path=/ and no Domain")]
    [InlineData("__Host-a=1; Secure", "__Host- prefix requires Secure, Path=/ and no Domain")]
    public void CallsOutCookiesBrowsersReject(string setCookie, string expectedWarning)
    {
        var row = Assert.Single(Rows(SessionWith([], [("Set-Cookie", setCookie)]), InspectorSide.Response));

        Assert.Contains("Warning: ", row.Value);
        Assert.Contains(expectedWarning, row.Value);
    }

    [Theory]
    [InlineData("a=1; SameSite=None; Secure")]
    [InlineData("__Secure-a=1; Secure")]
    [InlineData("__Host-a=1; Secure; Path=/")]
    public void ValidCookiesGetNoWarning(string setCookie)
    {
        var row = Assert.Single(Rows(SessionWith([], [("Set-Cookie", setCookie)]), InspectorSide.Response));

        Assert.DoesNotContain("Warning", row.Value);
    }

    [Fact]
    public void SortsBetweenHexAndNotes()
    {
        var session = SessionWith([("Cookie", "a=1")], []);
        var registry = InspectorRegistry.CreateDefault();

        var names = registry.GetApplicable(new InspectorContext(session, InspectorSide.Request)).Select(i => i.DisplayName).ToArray();

        Assert.Equal(new[] { "Headers", "Raw", "Hex", "Cookies" }, names);
    }
}

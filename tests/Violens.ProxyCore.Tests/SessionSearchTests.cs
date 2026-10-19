using System.IO.Compression;
using System.Text;
using Violens.ProxyCore.Http;
using Violens.ProxyCore.Sessions;
using Xunit;

namespace Violens.ProxyCore.Tests;

/// <summary>Find Sessions (<see cref="SessionSearch"/>).</summary>
public class SessionSearchTests
{
    private static Session Exchange()
    {
        var request = new CapturedRequest(
            "POST", "/api/login", "HTTP/1.1",
            [("Host", "auth.example.test"), ("Content-Type", "application/x-www-form-urlencoded")],
            Encoding.UTF8.GetBytes("user=alice&remember=true"));

        using var gzipped = new MemoryStream();
        using (var gzip = new GZipStream(gzipped, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(Encoding.UTF8.GetBytes("{\"token\":\"Secret-Token-42\"}"));
        }

        var response = new CapturedResponse(
            "HTTP/1.1", 401, "Unauthorized",
            [("Content-Type", "application/json; charset=utf-8"), ("Content-Encoding", "gzip"), ("WWW-Authenticate", "Bearer")],
            gzipped.ToArray());

        return new SessionStore().Add("auth.example.test", DateTimeOffset.UtcNow, request, response);
    }

    private static bool Finds(SessionSearchOptions options)
    {
        Assert.True(SessionSearch.TryCreate(options, out var search, out var error), error);
        return search!.Matches(Exchange());
    }

    [Theory]
    [InlineData("auth.example.test/api/login")] // the URL in the request line
    [InlineData("x-www-form-urlencoded")] // a request header
    [InlineData("remember=true")] // the request body
    [InlineData("401 Unauthorized")] // the status line
    [InlineData("WWW-Authenticate: Bearer")] // a response header
    [InlineData("secret-token-42")] // inside the gzipped response body
    public void LooksEverywhereByDefault(string text)
    {
        Assert.True(Finds(new SessionSearchOptions(text)));
    }

    [Fact]
    public void FindsNothingThatIsntThere()
    {
        Assert.False(Finds(new SessionSearchOptions("bob")));
    }

    [Fact]
    public void MatchCaseIsExact()
    {
        Assert.True(Finds(new SessionSearchOptions("Secret-Token", MatchCase: true)));
        Assert.False(Finds(new SessionSearchOptions("secret-token", MatchCase: true)));
    }

    [Theory]
    [InlineData("remember=true", SessionSearchTarget.Responses, false)]
    [InlineData("remember=true", SessionSearchTarget.Requests, true)]
    [InlineData("Secret-Token", SessionSearchTarget.Requests, false)]
    [InlineData("Secret-Token", SessionSearchTarget.Responses, true)]
    public void CanLookAtOneSideOnly(string text, SessionSearchTarget target, bool expected)
    {
        Assert.Equal(expected, Finds(new SessionSearchOptions(text, target)));
    }

    [Theory]
    [InlineData("Secret-Token", SessionSearchParts.Headers, false)]
    [InlineData("Secret-Token", SessionSearchParts.Bodies, true)]
    [InlineData("WWW-Authenticate", SessionSearchParts.Bodies, false)]
    [InlineData("WWW-Authenticate", SessionSearchParts.Headers, true)]
    public void CanLookAtHeadersOrBodiesOnly(string text, SessionSearchParts parts, bool expected)
    {
        Assert.Equal(expected, Finds(new SessionSearchOptions(text, Parts: parts)));
    }

    [Fact]
    public void RegularExpressions()
    {
        Assert.True(Finds(new SessionSearchOptions(@"token-\d+", UseRegex: true)));
        Assert.False(Finds(new SessionSearchOptions(@"token-\d{3}", UseRegex: true)));
        Assert.True(Finds(new SessionSearchOptions(@"^WWW-Authenticate:", UseRegex: true))); // each line is a line
    }

    [Theory]
    [InlineData("", false, "Type something")]
    [InlineData("([", true, "isn't a valid regular expression")]
    public void ExplainsWhatItCantSearchFor(string text, bool regex, string expected)
    {
        Assert.False(SessionSearch.TryCreate(new SessionSearchOptions(text, UseRegex: regex), out _, out var error));
        Assert.Contains(expected, error);
    }
}

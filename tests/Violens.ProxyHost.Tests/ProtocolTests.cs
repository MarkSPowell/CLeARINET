using System.Text;
using Violens.ProxyHost.Client;
using Xunit;

namespace Violens.ProxyHost.Tests;

/// <summary>The pipe protocol's framing and fields, and the client's message helpers.</summary>
public class ProtocolTests
{
    private static CapturedSession SampleSession()
    {
        var session = new CapturedSession
        {
            Id = 42,
            Host = "example.com",
            Scheme = "https",
            Url = "https://example.com/search?q=1",
            StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_123),
            Request = new ProxyRequest { Method = "POST", Target = "/search?q=1", Body = [1, 2, 3] },
            Response = new ProxyResponse { StatusCode = 404, ReasonPhrase = "Not Found", Body = Encoding.UTF8.GetBytes("nope") },
        };
        session.Request.Headers.Add(new("Host", "example.com"));
        session.Request.Headers.Add(new("Cookie", "a=1"));
        session.Request.Headers.Add(new("cookie", "b=2"));
        session.Response.Headers.Add(new("Content-Type", "text/plain"));
        session.Flags["ui-backcolor"] = "lightyellow";
        return session;
    }

    [Fact]
    public void ASessionRoundTrips()
    {
        var buffer = new MemoryStream();
        Protocol.WriteFrame(buffer, new object(), MessageType.SessionCaptured, writer => writer.WriteSession(SampleSession()));
        buffer.Position = 0;

        Assert.True(Protocol.TryReadFrame(buffer, out var type, out var fields));
        Assert.Equal(MessageType.SessionCaptured, type);
        var session = fields.ReadSession();

        Assert.Equal(42, session.Id);
        Assert.Equal("example.com", session.Host);
        Assert.Equal("https://example.com/search?q=1", session.Url);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_123), session.StartedAt);
        Assert.Equal("POST", session.Request.Method);
        Assert.Equal(new byte[] { 1, 2, 3 }, session.Request.Body);
        Assert.Equal(new[] { "Host", "Cookie", "cookie" }, session.Request.Headers.Select(h => h.Key));
        Assert.Equal(404, session.Response.StatusCode);
        Assert.Equal("nope", Encoding.UTF8.GetString(session.Response.Body));
        Assert.Equal("lightyellow", session.Flags["UI-BACKCOLOR"]);

        // Nothing left over, and the end of the stream reads as "closed".
        Assert.Equal(fields.BaseStream.Length, fields.BaseStream.Position);
        Assert.False(Protocol.TryReadFrame(buffer, out _, out _));
    }

    [Fact]
    public void NullableFieldsRoundTrip()
    {
        var buffer = new MemoryStream();
        Protocol.WriteFrame(buffer, new object(), MessageType.Log, writer =>
        {
            writer.WriteNullableString(null);
            writer.WriteNullableString("x");
            writer.WriteNullableResponse(null);
            writer.WriteNullableResponse(ProxyResponse.Text(201, "Created", "made"));
        });
        buffer.Position = 0;

        Assert.True(Protocol.TryReadFrame(buffer, out _, out var fields));
        Assert.Null(fields.ReadNullableString());
        Assert.Equal("x", fields.ReadNullableString());
        Assert.Null(fields.ReadNullableResponse());
        var response = fields.ReadNullableResponse();
        Assert.NotNull(response);
        Assert.Equal(201, response!.StatusCode);
        Assert.Equal("made", Encoding.UTF8.GetString(response.Body));
    }

    [Fact]
    public void AFrameCutShortIsAnError()
    {
        var buffer = new MemoryStream();
        Protocol.WriteFrame(buffer, new object(), MessageType.Log, writer => writer.Write("hello"));
        var truncated = new MemoryStream(buffer.ToArray(), 0, (int)buffer.Length - 2);

        Assert.Throws<EndOfStreamException>(() => Protocol.TryReadFrame(truncated, out _, out _));
    }

    [Fact]
    public void AnImpossibleLengthIsAnError()
    {
        var buffer = new MemoryStream([0xFF, 0xFF, 0xFF, 0x7F, 1]);

        Assert.Throws<InvalidDataException>(() => Protocol.TryReadFrame(buffer, out _, out _));
    }

    [Fact]
    public void SetHeaderReplacesInPlace()
    {
        var request = new ProxyRequest();
        request.Headers.Add(new("Host", "a"));
        request.Headers.Add(new("Accept", "1"));
        request.Headers.Add(new("accept", "2"));
        request.Headers.Add(new("User-Agent", "x"));

        request.SetHeader("ACCEPT", "*/*");

        Assert.Equal(new[] { "Host", "ACCEPT", "User-Agent" }, request.Headers.Select(h => h.Key));
        Assert.Equal("*/*", request.GetHeader("accept"));
        Assert.Null(request.GetHeader("Cookie"));
    }

    [Fact]
    public void SetBodyTextFixesTheFramingHeaders()
    {
        var response = new ProxyResponse();
        response.Headers.Add(new("Content-Encoding", "gzip"));
        response.Headers.Add(new("Transfer-Encoding", "chunked"));
        response.Headers.Add(new("Content-Length", "999"));

        response.SetBodyText("héllo");

        Assert.Equal(6, response.Body.Length);
        Assert.Equal("6", response.GetHeader("Content-Length"));
        Assert.Null(response.GetHeader("Content-Encoding"));
        Assert.Null(response.GetHeader("Transfer-Encoding"));
    }
}

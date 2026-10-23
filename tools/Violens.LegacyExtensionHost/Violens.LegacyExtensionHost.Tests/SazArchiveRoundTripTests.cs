using System;
using System.IO;
using System.Text;
using Violens.CompatShim;
using Xunit;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// Writes and reads a temp .saz through <see cref="Utilities.WriteSessionArchive"/>
/// and <see cref="Utilities.ReadSessionArchive"/>, the entry points
/// extensions call. Self-contained, so it always runs.
/// </summary>
public sealed class SazArchiveRoundTripTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), "violens-saz-tests-" + Guid.NewGuid() + ".saz");

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void WriteThenRead_RoundTripsRequestResponseHeadersBodiesAndFlags()
    {
        var original = new Session { id = 1 };
        original.oRequest.headers.HTTPMethod = "POST";
        original.oRequest.headers.RequestPath = "/api/data?x=1";
        original.oRequest.headers["Host"] = "api.example.com";
        original.oRequest.headers["Content-Type"] = "application/json";
        original.requestBodyBytes = Encoding.UTF8.GetBytes("{\"a\":1}");

        original.oResponse.headers.HTTPResponseStatus = "HTTP/1.1 200 OK";
        original.oResponse.headers["Content-Type"] = "text/html; charset=utf-8";
        original.responseBodyBytes = Encoding.UTF8.GetBytes("Hello, world!");

        original["https"] = "true";

        var wrote = Utilities.WriteSessionArchive(_tempFile, new[] { original }, password: null, encrypt: false);
        Assert.True(wrote);
        Assert.True(File.Exists(_tempFile));

        var roundTripped = Utilities.ReadSessionArchive(_tempFile, decrypt: false);

        var session = Assert.Single(roundTripped);
        Assert.Equal("POST", session.oRequest.headers.HTTPMethod);
        Assert.Equal("api.example.com", session.host);
        Assert.Equal("/api/data?x=1", session.PathAndQuery);
        Assert.Equal("api.example.com/api/data?x=1", session.url);
        // The scheme comes from the "https" flag.
        Assert.Equal("https://api.example.com/api/data?x=1", session.fullUrl);
        Assert.Equal("application/json", session.oRequest.headers["Content-Type"]);
        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(session.requestBodyBytes));

        Assert.Equal("HTTP/1.1 200 OK", session.oResponse.headers.HTTPResponseStatus);
        Assert.Equal(200, session.responseCode);
        Assert.Equal("text/html; charset=utf-8", session.oResponse.headers["Content-Type"]);
        Assert.Equal("Hello, world!", Encoding.UTF8.GetString(session.responseBodyBytes));

        Assert.Equal("true", session["https"]);
    }

    [Fact]
    public void WriteThenRead_WithZeroSessions_RoundTripsCleanly()
    {
        var wrote = Utilities.WriteSessionArchive(_tempFile, Array.Empty<Session>(), password: null, encrypt: false);
        Assert.True(wrote);

        var roundTripped = Utilities.ReadSessionArchive(_tempFile, decrypt: false);

        Assert.Empty(roundTripped);
    }

    [Fact]
    public void Write_WithEncryptRequested_ThrowsRatherThanSilentlyWritingUnprotected()
    {
        // Encryption isn't supported, so this must throw rather than write an
        // unprotected file.
        Assert.Throws<NotSupportedException>(() =>
            Utilities.WriteSessionArchive(_tempFile, new[] { new Session { id = 1 } }, password: null, encrypt: true));

        Assert.False(File.Exists(_tempFile));
    }

    [Fact]
    public void Write_WithPasswordRequested_ThrowsRatherThanSilentlyWritingUnprotected()
    {
        Assert.Throws<NotSupportedException>(() =>
            Utilities.WriteSessionArchive(_tempFile, new[] { new Session { id = 1 } }, password: "hunter2", encrypt: false));
    }

    [Fact]
    public void Read_OfAnUnreadableArchive_ThrowsRatherThanSilentlyReturningEmpty()
    {
        // An encrypted archive looks corrupt to ZipArchive too, so garbage bytes
        // stand in for one.
        File.WriteAllBytes(_tempFile, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

        Assert.Throws<NotSupportedException>(() => Utilities.ReadSessionArchive(_tempFile, decrypt: false));
    }

    [Fact]
    public void Read_OfAMissingFile_ReturnsEmptyRatherThanThrowing()
    {
        // A missing file means nothing to load, not an error.
        var missingFile = Path.Combine(Path.GetTempPath(), "violens-saz-tests-missing-" + Guid.NewGuid() + ".saz");

        var result = Utilities.ReadSessionArchive(missingFile, decrypt: false);

        Assert.Empty(result);
    }
}

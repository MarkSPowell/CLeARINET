using System;
using System.IO;
using System.Text;
using Clearinet.CompatShim;
using Xunit;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Fully self-contained -- builds its own synthetic sessions and writes/
/// reads a real temp .saz file, no external file or real extension DLL
/// needed, so this always runs (including in CI with none of the real
/// samples present). Exercises the real <c>SazArchive</c> implementation
/// through <see cref="Utilities.WriteSessionArchive"/>/
/// <see cref="Utilities.ReadSessionArchive"/> -- the same public entry
/// points a real extension (SAZClipboard, Differ) actually calls -- rather
/// than reaching into the internal <c>SazArchive</c> class directly.
/// </summary>
public sealed class SazArchiveRoundTripTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), "clearinet-saz-tests-" + Guid.NewGuid() + ".saz");

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
        // Scheme is guessed from the "https" session flag (see
        // SazArchive.GuessScheme's own remarks) -- not otherwise present in
        // a relative-form request line.
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
        // Locks in the documented gap (see SazArchive's own remarks): .NET's
        // ZipArchive can't write encrypted entries, so this must fail
        // loudly, not silently produce an unprotected file while reporting
        // success.
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
        // A real password-protected .saz would hit the same code path (see
        // SazArchive.Read's own remarks: .NET's ZipArchive can't tell
        // "encrypted" apart from "corrupt," both surface as
        // InvalidDataException) -- garbage bytes are a self-contained stand-in
        // for either, without needing a real encrypted sample file.
        File.WriteAllBytes(_tempFile, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

        Assert.Throws<NotSupportedException>(() => Utilities.ReadSessionArchive(_tempFile, decrypt: false));
    }

    [Fact]
    public void Read_OfAMissingFile_ReturnsEmptyRatherThanThrowing()
    {
        // Deliberately different from the unreadable-archive case above --
        // see SazArchive.Read's own early-return for a missing/empty
        // filename, which is a normal "nothing to load yet" case, not a
        // failure.
        var missingFile = Path.Combine(Path.GetTempPath(), "clearinet-saz-tests-missing-" + Guid.NewGuid() + ".saz");

        var result = Utilities.ReadSessionArchive(missingFile, decrypt: false);

        Assert.Empty(result);
    }
}

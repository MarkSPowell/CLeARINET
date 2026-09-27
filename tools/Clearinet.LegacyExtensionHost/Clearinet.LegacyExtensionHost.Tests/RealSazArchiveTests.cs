using System;
using Clearinet.CompatShim;
using Xunit;
using Xunit.Abstractions;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Optional: reads a real, previously-captured .saz file if one is pointed
/// at via the <c>CLEARINET_LEGACY_TEST_SAZ</c> environment variable (e.g.
/// the 87-session capture already used to validate this shim by hand this
/// session), skipping gracefully when it isn't set -- this is on top of,
/// not instead of, <see cref="SazArchiveRoundTripTests"/>'s fully
/// self-contained coverage, which is what actually runs everywhere
/// (including CI). A real capture inevitably has things synthetic test
/// data doesn't (odd encodings, missing headers, binary/compressed
/// bodies), so this is worth pointing at real captures by hand from time
/// to time even though it can't be relied on to run automatically.
/// </summary>
public sealed class RealSazArchiveTests
{
    private const string SazFileEnvironmentVariable = "CLEARINET_LEGACY_TEST_SAZ";
    private readonly ITestOutputHelper _output;

    public RealSazArchiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Read_OfARealCapturedArchive_LoadsAtLeastOneSessionWithoutThrowing()
    {
        var path = Environment.GetEnvironmentVariable(SazFileEnvironmentVariable);
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            _output.WriteLine(
                $"SKIPPED (inert pass): set {SazFileEnvironmentVariable} to a real .saz file's path to run this test " +
                "against real captured traffic instead of only synthetic data.");
            return;
        }

        var sessions = Utilities.ReadSessionArchive(path, decrypt: false);

        Assert.NotEmpty(sessions);
        foreach (var session in sessions)
        {
            // Same guarantee HTTPRequestHeaders/HTTPResponseHeaders now
            // promise everywhere (see their own remarks, added after the
            // Differ.dll investigation) -- these should never come back
            // null for a session this shim itself parsed.
            Assert.NotNull(session.oRequest?.headers?.HTTPMethod);
            Assert.NotNull(session.oResponse?.headers?.HTTPResponseStatus);
        }
    }
}

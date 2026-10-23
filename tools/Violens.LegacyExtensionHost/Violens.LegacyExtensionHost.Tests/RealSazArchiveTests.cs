using System;
using Violens.CompatShim;
using Xunit;
using Xunit.Abstractions;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// Reads a real capture named by <c>VIOLENS_LEGACY_TEST_SAZ</c>, if set; real
/// captures have quirks synthetic data doesn't. Skipped otherwise;
/// <see cref="SazArchiveRoundTripTests"/> always runs.
/// </summary>
public sealed class RealSazArchiveTests
{
    private const string SazFileEnvironmentVariable = "VIOLENS_LEGACY_TEST_SAZ";
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
            // Never null for a session the shim parsed.
            Assert.NotNull(session.oRequest?.headers?.HTTPMethod);
            Assert.NotNull(session.oResponse?.headers?.HTTPResponseStatus);
        }
    }
}

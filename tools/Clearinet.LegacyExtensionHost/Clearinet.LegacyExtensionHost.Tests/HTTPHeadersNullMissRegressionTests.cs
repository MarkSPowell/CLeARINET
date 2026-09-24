using Clearinet.CompatShim;
using Xunit;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Locks in <see cref="HTTPHeaders"/>'s indexer's confirmed-correct
/// null-on-miss behavior as a regression guard. This isn't a bug in the
/// shim -- it's the confirmed reason <c>Differ.dll</c> throws a real
/// <see cref="System.NullReferenceException"/> on a session that genuinely
/// lacks a Content-Type header (see the design doc's "Root cause found: a
/// real gap in Differ.dll itself, not this shim" entry, itself confirmed
/// against real Fiddler's own public API docs). If a future change to
/// <see cref="HTTPHeaders"/> ever made a miss return
/// <see cref="string.Empty"/> instead of <c>null</c> to "fix" that crash,
/// it would silently diverge from real Fiddler's own documented contract
/// -- this test exists to catch that specific regression, not to imply the
/// crash itself should ever be "fixed" here (see the design doc for why
/// synthesizing a fake header would misrepresent real captured traffic).
/// </summary>
public sealed class HTTPHeadersNullMissRegressionTests
{
    [Fact]
    public void Indexer_OnAMissingHeader_ReturnsNullNotEmptyString()
    {
        var headers = new HTTPRequestHeaders();

        var value = headers["Content-Type"];

        Assert.Null(value);
    }

    [Fact]
    public void Indexer_OnAPresentHeader_ReturnsItsValue()
    {
        var headers = new HTTPRequestHeaders();
        headers["Content-Type"] = "text/plain";

        Assert.Equal("text/plain", headers["Content-Type"]);
    }

    [Fact]
    public void Indexer_HeaderNameLookupIsCaseInsensitive()
    {
        var headers = new HTTPRequestHeaders();
        headers["Content-Type"] = "text/plain";

        Assert.Equal("text/plain", headers["content-type"]);
        Assert.Equal("text/plain", headers["CONTENT-TYPE"]);
    }

    [Fact]
    public void Exists_MatchesWhetherTheIndexerWouldReturnNull()
    {
        var headers = new HTTPRequestHeaders();
        headers["Content-Type"] = "text/plain";

        Assert.True(headers.Exists("Content-Type"));
        Assert.False(headers.Exists("X-Not-Present"));
    }

    [Fact]
    public void Setter_OnAnExistingHeader_ReplacesRatherThanDuplicates()
    {
        var headers = new HTTPRequestHeaders();
        headers["Content-Type"] = "text/plain";
        headers["Content-Type"] = "application/json";

        Assert.Equal("application/json", headers["Content-Type"]);
        Assert.DoesNotContain("text/plain", headers.ToString());
    }
}

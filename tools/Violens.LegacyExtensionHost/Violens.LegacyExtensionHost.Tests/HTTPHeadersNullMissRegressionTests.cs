using Violens.CompatShim;
using Xunit;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// The indexer returns null for a missing header, as Fiddler documents.
/// That's why Differ.dll crashes on a session without Content-Type, but
/// returning an empty string instead would break the documented contract,
/// so this guards against that "fix".
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

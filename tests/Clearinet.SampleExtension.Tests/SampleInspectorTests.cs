using System.Text;
using Clearinet.Compatibility.FiddlerScript;
using Clearinet.SampleExtension;
using Xunit;

namespace Clearinet.SampleExtension.Tests;

/// <summary>
/// Covers both <see cref="SampleRequestInspector"/> and
/// <see cref="SampleResponseInspector"/> -- structurally identical twins
/// (see either class's own remarks on why they're separate types), so one
/// parameterized-by-hand pass through each is enough; there's nothing
/// side-specific to their behavior worth testing twice.
/// </summary>
public class SampleInspectorTests
{
    [Fact]
    public void RequestInspector_SettingBody_PrependsSummaryAndSetsDirty()
    {
        var inspector = new SampleRequestInspector
        {
            headers = new ExchangeHeaders([("Content-Type", "text/plain"), ("Host", "api.example.com")]),
        };
        Assert.False(inspector.bDirty);

        inspector.body = "hello"u8.ToArray();

        Assert.True(inspector.bDirty);
        var text = Encoding.UTF8.GetString(inspector.body!);
        Assert.StartsWith("[SampleRequestInspector: 2 header(s), 5 original body byte(s)]\n", text);
        Assert.EndsWith("hello", text);
    }

    [Fact]
    public void RequestInspector_Clear_ResetsHeadersBodyAndDirty()
    {
        var inspector = new SampleRequestInspector
        {
            headers = new ExchangeHeaders([("Host", "api.example.com")]),
            body = "hello"u8.ToArray(),
        };

        inspector.Clear();

        Assert.Null(inspector.headers);
        Assert.Null(inspector.body);
        Assert.False(inspector.bDirty);
    }

    [Fact]
    public void RequestInspector_bReadOnly_RoundTrips()
    {
        var inspector = new SampleRequestInspector();

        inspector.bReadOnly = true;

        Assert.True(inspector.bReadOnly);
    }

    [Fact]
    public void ResponseInspector_SettingBody_PrependsSummaryAndSetsDirty()
    {
        var inspector = new SampleResponseInspector
        {
            headers = new ExchangeHeaders([("Content-Type", "text/plain")]),
        };
        Assert.False(inspector.bDirty);

        inspector.body = "world"u8.ToArray();

        Assert.True(inspector.bDirty);
        var text = Encoding.UTF8.GetString(inspector.body!);
        Assert.StartsWith("[SampleResponseInspector: 1 header(s), 5 original body byte(s)]\n", text);
        Assert.EndsWith("world", text);
    }

    [Fact]
    public void ResponseInspector_Clear_ResetsHeadersBodyAndDirty()
    {
        var inspector = new SampleResponseInspector
        {
            headers = new ExchangeHeaders([("Host", "api.example.com")]),
            body = "world"u8.ToArray(),
        };

        inspector.Clear();

        Assert.Null(inspector.headers);
        Assert.Null(inspector.body);
        Assert.False(inspector.bDirty);
    }
}

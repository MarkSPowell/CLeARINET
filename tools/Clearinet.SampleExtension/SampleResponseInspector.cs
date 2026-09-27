using System.Text;
using Clearinet.Compatibility.Extensions;
using Clearinet.Compatibility.FiddlerScript;

namespace Clearinet.SampleExtension;

/// <summary>
/// The response-side twin of <see cref="SampleRequestInspector"/> -- see
/// that class's own remarks. Kept as a separate class rather than one type
/// implementing both <see cref="IRequestInspector2"/> and
/// <see cref="IResponseInspector2"/>: <c>ExtensionHost</c> would register a
/// dual-implementing instance into both
/// <c>ExtensionHost.RequestInspectors</c> and
/// <c>ExtensionHost.ResponseInspectors</c>, and the two
/// <c>Inspector2Adapter</c>-wrapped tabs that produces would then share one
/// mutable <see cref="body"/>/<see cref="headers"/> pair across what's
/// supposed to be two independent inspections -- two small, separate
/// classes avoid that shared-state trap entirely, at the cost of a little
/// duplication that's fine for a sample this size.
/// </summary>
public sealed class SampleResponseInspector : IResponseInspector2
{
    private byte[]? _body;

    public ExchangeHeaders? headers { get; set; }

    public byte[]? body
    {
        get => _body;
        set
        {
            if (value is null)
            {
                _body = null;
                return;
            }

            var headerCount = headers?.ToList().Count ?? 0;
            var summary = Encoding.UTF8.GetBytes(
                $"[SampleResponseInspector: {headerCount} header(s), {value.Length} original body byte(s)]\n");
            _body = [.. summary, .. value];
            bDirty = true;
        }
    }

    public bool bDirty { get; private set; }

    public bool bReadOnly { get; set; }

    public void Clear()
    {
        headers = null;
        _body = null;
        bDirty = false;
    }
}

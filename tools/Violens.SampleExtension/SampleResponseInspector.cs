using System.Text;
using Violens.Compatibility.Extensions;
using Violens.Compatibility.FiddlerScript;

namespace Violens.SampleExtension;

/// <summary>
/// The response-side copy of <see cref="SampleRequestInspector"/>. A
/// separate class, since one instance registered on both sides would share
/// its body between two tabs.
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

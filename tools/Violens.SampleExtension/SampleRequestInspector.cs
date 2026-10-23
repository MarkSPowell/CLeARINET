using System.Text;
using Violens.Compatibility.Extensions;
using Violens.Compatibility.FiddlerScript;

namespace Violens.SampleExtension;

/// <summary>
/// An original <see cref="IRequestInspector2"/>. Prepends a summary line to
/// the body it's given, so the tab shows the "set body, read it back"
/// round trip working.
/// </summary>
public sealed class SampleRequestInspector : IRequestInspector2
{
    private byte[]? _body;

    public ExchangeHeaders? headers { get; set; }

    /// <summary>Setting it prepends a summary line; reading it returns the changed bytes.</summary>
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
                $"[SampleRequestInspector: {headerCount} header(s), {value.Length} original body byte(s)]\n");
            _body = [.. summary, .. value];
            bDirty = true;
        }
    }

    public bool bDirty { get; private set; }

    public bool bReadOnly { get; set; }

    /// <summary>Resets every piece of state this inspector holds -- called between exchanges, matching real Fiddler's own <c>Clear()</c> contract.</summary>
    public void Clear()
    {
        headers = null;
        _body = null;
        bDirty = false;
    }
}

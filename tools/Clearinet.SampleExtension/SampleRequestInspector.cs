using System.Text;
using Clearinet.Compatibility.Extensions;
using Clearinet.Compatibility.FiddlerScript;

namespace Clearinet.SampleExtension;

/// <summary>
/// An original <see cref="IRequestInspector2"/> -- exercises every
/// <see cref="IInspector2"/> member the exact way
/// <c>Inspector2Adapter</c> actually uses them: <see cref="headers"/> is
/// set first, then <see cref="body"/>, and whatever this class does in
/// response to that is what ends up in the request-side "SampleRequestInspector"
/// tab once <c>ExtensionHost</c> discovers it and
/// <c>Inspector2Adapter.ForRequest</c> wraps it. Prepends a one-line
/// summary to whatever body it's given, so the transformation is visible
/// in that tab -- proof the "set body, read body back" round trip
/// <see cref="IInspector2"/>'s own remarks describe actually works against
/// a real, separately-compiled extension, not just the interfaces in
/// isolation.
/// </summary>
public sealed class SampleRequestInspector : IRequestInspector2
{
    private byte[]? _body;

    public ExchangeHeaders? headers { get; set; }

    /// <summary>
    /// Setting this prepends a summary line (header count, original body
    /// length) and flips <see cref="bDirty"/> -- reading it back afterward
    /// returns the transformed bytes, not the ones that were set, which is
    /// the whole point: this is what lets a real inspector rewrite what the
    /// host displays.
    /// </summary>
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

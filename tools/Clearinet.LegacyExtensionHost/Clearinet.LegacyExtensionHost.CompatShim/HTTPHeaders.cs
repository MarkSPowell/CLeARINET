using System;
using System.Collections.Generic;
using System.Text;

namespace Clearinet.CompatShim;

/// <summary>
/// Base class for <see cref="HTTPRequestHeaders"/>/<see cref="HTTPResponseHeaders"/>.
/// Members confirmed by metadata across the five samples inspected for this
/// shim: <c>ExistsAndContains(string,string)</c> (AustralianImages,
/// ContentBlock, JSFormat), indexer get/set (ContentBlock, JSFormat), and
/// <c>Exists(string)</c> (ContentBlock). Backed by a simple ordered,
/// case-insensitive name/value store -- real Fiddler's own internal storage
/// shape is not reproduced here (unknown, and irrelevant to binary
/// compatibility, which only depends on the public member signatures these
/// extensions actually call).
/// </summary>
public class HTTPHeaders
{
    private readonly List<KeyValuePair<string, string>> _headers = new();

    /// <summary>metadata: <c>Fiddler.HTTPHeaders.bool ExistsAndContains(string, string)</c> -- AustralianImages, ContentBlock, JSFormat.</summary>
    public bool ExistsAndContains(string headerName, string containsValue)
    {
        var value = this[headerName];
        return value != null && value.IndexOf(containsValue, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>metadata: <c>Fiddler.HTTPHeaders.bool Exists(string)</c> -- ContentBlock.</summary>
    public bool Exists(string headerName) => this[headerName] != null;

    /// <summary>
    /// metadata: <c>get_Item(string)</c>/<c>set_Item(string, string)</c> --
    /// ContentBlock, JSFormat. Confirmed against real Fiddler's own public
    /// API documentation: the getter returns <c>null</c>, not an empty
    /// string, when the header doesn't exist -- this matches that, so a
    /// caller not null-checking a missing header's own bug, not a
    /// divergence from real Fiddler here.
    /// </summary>
    public string this[string headerName]
    {
        get
        {
            foreach (var kvp in _headers)
            {
                if (string.Equals(kvp.Key, headerName, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            // Diagnostic only -- doesn't change behavior (still returns
            // null, matching real Fiddler). Added while chasing a real
            // NullReferenceException inside Differ.dll's own code with no
            // shim-method frames in the stack: a caller dereferencing a
            // null header value straight off this indexer would look
            // exactly like that, and this makes the last such miss before
            // a crash visible on the Log tab without reading Differ's IL.
            FiddlerApplication.Log.LogFormat("HTTPHeaders[\"{0}\"]: not found, returning null.", new object[] { headerName });
            return null;
        }
        set
        {
            for (var i = 0; i < _headers.Count; i++)
            {
                if (string.Equals(_headers[i].Key, headerName, StringComparison.OrdinalIgnoreCase))
                {
                    _headers[i] = new KeyValuePair<string, string>(headerName, value);
                    return;
                }
            }
            _headers.Add(new KeyValuePair<string, string>(headerName, value));
        }
    }

    /// <summary>
    /// Not part of the binary-compatibility surface either (no sample's
    /// metadata calls anything with this shape) -- added for the session
    /// bridge (see the design doc's "Session bridge" section): projecting a
    /// <see cref="Session"/> back into a <c>WireRequest</c>/<c>WireResponse</c>
    /// after an extension's <c>IAutoTamper</c> hook has possibly added,
    /// changed, or left alone any header needs a way to read the whole
    /// current set back out, which the name-keyed indexer above can't do by
    /// itself. Returns the same ordered pairs <see cref="ToString"/> already
    /// renders, just structured instead of formatted.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> AllHeaders => _headers;

    /// <summary>
    /// Not confirmed against any sample's metadata -- a plain-text rendering
    /// convenience for the legacy host's own UI/logging, not part of the
    /// binary-compatibility surface.
    /// </summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var kvp in _headers)
        {
            sb.Append(kvp.Key).Append(": ").Append(kvp.Value).Append("\r\n");
        }
        return sb.ToString();
    }
}

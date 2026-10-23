using System;
using System.Collections.Generic;
using System.Text;

namespace Violens.CompatShim;

/// <summary>
/// Base of the request and response header classes: <c>ExistsAndContains</c>,
/// the indexer and <c>Exists</c>, as the samples use them. An ordered,
/// case-insensitive list underneath.
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

    /// <summary>Returns null for a missing header, as Fiddler's documentation says.</summary>
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

            // Diagnostic only: makes a null that a caller then dereferences visible
            // on the Log tab.
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

    /// <summary>Every header in order, for sending sessions back over the bridge.</summary>
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

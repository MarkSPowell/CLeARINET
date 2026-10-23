namespace Violens.CompatShim;

/// <summary>
/// <c>HTTPMethod</c> (a field), <c>UriScheme</c> and <c>RequestPath</c>,
/// used by SAZClipboard. All default to empty rather than null: Fiddler
/// never exposes null for these, and Differ.dll crashed when they were.
/// </summary>
public sealed class HTTPRequestHeaders : HTTPHeaders
{
    /// <summary>metadata: field, not a property -- <c>Fiddler.HTTPRequestHeaders.string HTTPMethod</c>.</summary>
    public string HTTPMethod = string.Empty;

    /// <summary>metadata: <c>Fiddler.HTTPRequestHeaders.string get_UriScheme()</c>.</summary>
    public string UriScheme { get; set; } = string.Empty;

    /// <summary>metadata: <c>Fiddler.HTTPRequestHeaders.string get_RequestPath()</c>.</summary>
    public string RequestPath { get; set; } = string.Empty;
}

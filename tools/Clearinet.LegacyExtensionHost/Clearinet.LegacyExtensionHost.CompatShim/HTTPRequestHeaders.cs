namespace Clearinet.CompatShim;

/// <summary>
/// Members confirmed by metadata: <c>HTTPMethod</c> field (SAZClipboard --
/// <c>Fiddler.HTTPRequestHeaders.string HTTPMethod (field)</c>),
/// <c>UriScheme</c>/<c>RequestPath</c> properties (SAZClipboard).
///
/// All three default to <see cref="string.Empty"/>, not <c>null</c> --
/// found the hard way: real testing against <c>Differ.dll</c> threw a
/// <see cref="System.NullReferenceException"/> directly inside its own
/// <c>DiffView.AddSessions</c> with no shim-method frames anywhere in the
/// stack, which points at a plain field/property being null rather than a
/// call into this shim failing. Real Fiddler's own session model wouldn't
/// expose a null method/scheme/path for any loaded session (worst case,
/// empty), and third-party extensions were written against that guarantee
/// -- so this shim should uphold it too, everywhere a
/// <see cref="Fiddler.Session"/> exists, not only for sessions
/// <c>SazArchive</c> successfully parsed a raw request for.
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

namespace Clearinet.LegacyExtensionHost.Bridge;

/// <summary>
/// One header name/value pair on the wire. Deliberately not a
/// <c>KeyValuePair&lt;string,string&gt;</c> or a tuple -- System.Text.Json
/// serializes a plain class with named properties far more predictably
/// across the netstandard2.0/net48/net10.0 split this project spans than
/// either of those (a tuple's <c>Item1</c>/<c>Item2</c> property names are
/// an implementation detail, not a contract worth depending on).
/// </summary>
public sealed class WireHeader
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Wire shape of <c>Clearinet.ProxyCore.Http.CapturedRequest</c> --
/// deliberately a separate, independent type rather than referencing that
/// record directly (see this project's own <c>.csproj</c> remarks on why
/// nothing net10.0/net48-specific crosses this boundary). Field names and
/// shapes are kept a straightforward mirror on purpose, so mapping in
/// either direction stays a plain property-by-property copy, not a
/// translation.
/// </summary>
public sealed class WireRequest
{
    public string Method { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string HttpVersion { get; set; } = string.Empty;

    public List<WireHeader> Headers { get; set; } = new();

    /// <summary>
    /// System.Text.Json serializes <c>byte[]</c> as a base64 string by
    /// default on every target this project spans -- no custom converter
    /// needed for that, just something worth being explicit about here
    /// since it's not visible from the type alone.
    /// </summary>
    public byte[] Body { get; set; } = Array.Empty<byte>();
}

/// <summary>Wire shape of <c>Clearinet.ProxyCore.Http.CapturedResponse</c> -- see <see cref="WireRequest"/>'s own remarks.</summary>
public sealed class WireResponse
{
    public string HttpVersion { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string ReasonPhrase { get; set; } = string.Empty;

    public List<WireHeader> Headers { get; set; } = new();

    public byte[] Body { get; set; } = Array.Empty<byte>();
}

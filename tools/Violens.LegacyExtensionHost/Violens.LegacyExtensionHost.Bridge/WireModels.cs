namespace Violens.LegacyExtensionHost.Bridge;

/// <summary>A header on the wire: a class with named properties, which serializes the same on every target.</summary>
public sealed class WireHeader
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// <c>CapturedRequest</c> on the wire: a separate type mirroring it, so
/// nothing framework-specific crosses between net48 and net10.0.
/// </summary>
public sealed class WireRequest
{
    public string Method { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string HttpVersion { get; set; } = string.Empty;

    public List<WireHeader> Headers { get; set; } = new();

    /// <summary>Serialized as base64.</summary>
    public byte[] Body { get; set; } = Array.Empty<byte>();
}

/// <summary><c>CapturedResponse</c> on the wire.</summary>
public sealed class WireResponse
{
    public string HttpVersion { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string ReasonPhrase { get; set; } = string.Empty;

    public List<WireHeader> Headers { get; set; } = new();

    public byte[] Body { get; set; } = Array.Empty<byte>();
}

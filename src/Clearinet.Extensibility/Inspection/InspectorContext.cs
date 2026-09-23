using Clearinet.ProxyCore.Sessions;

namespace Clearinet.Extensibility.Inspection;

/// <summary>
/// What an inspector gets to look at: one side of one captured session.
/// Deliberately narrower than handing over the whole <see cref="Session"/>
/// -- a request-side inspector has no business reading the response body,
/// and this makes that structural rather than a convention every inspector
/// author has to remember and honor.
/// </summary>
public sealed class InspectorContext
{
    public InspectorContext(Session session, InspectorSide side)
    {
        Session = session;
        Side = side;
    }

    public Session Session { get; }

    public InspectorSide Side { get; }

    public IReadOnlyList<(string Name, string Value)> Headers =>
        Side == InspectorSide.Request ? Session.Request.Headers : Session.Response.Headers;

    public byte[] Body =>
        Side == InspectorSide.Request ? Session.Request.Body : Session.Response.Body;

    /// <summary>
    /// First matching header value, case-insensitive per RFC 9110, or null
    /// if absent. A convenience most inspectors need (Content-Type,
    /// Content-Encoding) without re-implementing the case-insensitive scan
    /// themselves.
    /// </summary>
    public string? FindHeader(string name)
    {
        foreach (var (headerName, value) in Headers)
        {
            if (string.Equals(headerName, name, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }
}

using Violens.ProxyCore.Sessions;

namespace Violens.Extensibility.Inspection;

/// <summary>One side of one session: all an inspector gets, so a request inspector can't read the response.</summary>
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

    /// <summary>The first header with this name (case-insensitive), or null.</summary>
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

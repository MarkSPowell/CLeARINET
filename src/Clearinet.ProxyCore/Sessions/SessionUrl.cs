using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Sessions;

/// <summary>A session's full request URL.</summary>
public static class SessionUrl
{
    /// <summary>
    /// The request's full URL. CLeARINET captures HTTPS, so a relative target
    /// is https://, using the Host header (which carries any non-default
    /// port) or else the session's host. An absolute target is returned as is.
    /// </summary>
    public static string Of(Session session)
    {
        var target = session.Request.Target;
        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return target;
        }

        var authority = ContentDecoder.FindHeader(session.Request.Headers, "Host") ?? session.Host;
        return $"https://{authority}{(target.StartsWith('/') ? target : "/" + target)}";
    }
}

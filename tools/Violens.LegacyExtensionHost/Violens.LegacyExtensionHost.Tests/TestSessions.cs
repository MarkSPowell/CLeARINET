using System.Text;
using Violens.CompatShim;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// Well-formed synthetic sessions, with a Content-Type on both sides
/// (Differ.dll crashes without one, which isn't what these tests check).
/// </summary>
internal static class TestSessions
{
    public static Session CreateWellFormed(int id, bool includeContentType = true)
    {
        var session = new Session
        {
            id = id,
            host = "example.com",
            url = "example.com/index.html",
            fullUrl = "http://example.com/index.html",
            PathAndQuery = "/index.html",
            responseCode = 200,
            state = SessionStates.Done,
        };

        session.oRequest.headers.HTTPMethod = "GET";
        session.oRequest.headers["Host"] = "example.com";
        if (includeContentType)
        {
            session.oRequest.headers["Content-Type"] = "text/plain";
        }

        session.oResponse.headers.HTTPResponseStatus = "200 OK";
        if (includeContentType)
        {
            session.oResponse.headers["Content-Type"] = "text/html; charset=utf-8";
        }
        session.oResponse.headers["Content-Length"] = "13";

        session.requestBodyBytes = System.Array.Empty<byte>();
        session.responseBodyBytes = Encoding.UTF8.GetBytes("Hello, world!");

        return session;
    }
}

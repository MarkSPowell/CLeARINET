using System.Text;
using Clearinet.CompatShim;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Builds well-formed synthetic <see cref="Session"/> instances for tests
/// that exercise real extension behavior (not just loading). Deliberately
/// gives every session a Content-Type on both request and response by
/// default -- the confirmed-real gap this project found (a session
/// genuinely missing Content-Type crashes <c>Differ.dll</c>'s own code,
/// not this shim -- see the design doc's "Root cause found" entry) is a
/// property of specific real captured traffic, not something a synthetic
/// smoke-test session needs to reproduce to usefully check that an
/// extension's hooks run without throwing.
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

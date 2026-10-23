using System;
using Violens.CompatShim;
using Xunit;

namespace Violens.LegacyExtensionHost.Tests;

/// <summary>
/// Starts <see cref="SessionBridgeServer"/> once per test run with one fake
/// extension. It's the only place that may: the pipe allows a fixed number
/// of server instances, and a second start would exceed it.
/// </summary>
public sealed class SessionBridgeServerFixture
{
    /// <summary>
    /// Echoes a marker request header into the response and replaces response
    /// bodies with a fixed string.
    /// </summary>
    public sealed class EchoAutoTamper : IAutoTamper
    {
        public void OnLoad() { }

        public void OnBeforeUnload() { }

        public void AutoTamperRequestBefore(Session oSession) =>
            oSession.oRequest.headers["X-Echo-Seen"] = oSession.oRequest.headers["X-Echo"] ?? string.Empty;

        public void AutoTamperRequestAfter(Session oSession) =>
            oSession.oRequest.headers["X-Echo-After-Seen"] = "true";

        public void AutoTamperResponseBefore(Session oSession) =>
            oSession.utilSetResponseBody("rewritten-by-EchoAutoTamper");

        public void AutoTamperResponseAfter(Session oSession) { }

        public void OnBeforeReturningError(Session oSession) { }
    }

    public SessionBridgeServerFixture()
    {
        SessionBridgeServer.Start(new IAutoTamper[] { new EchoAutoTamper() }, _ => { });
    }
}

[CollectionDefinition(Name)]
public sealed class SessionBridgeServerCollection : ICollectionFixture<SessionBridgeServerFixture>
{
    public const string Name = "SessionBridgeServer";
}

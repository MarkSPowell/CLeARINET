using System;
using Clearinet.CompatShim;
using Xunit;

namespace Clearinet.LegacyExtensionHost.Tests;

/// <summary>
/// Starts <see cref="SessionBridgeServer"/> exactly once per test run, with
/// one small, test-observable fake <see cref="IAutoTamper"/> loaded --
/// shared across every <c>[Fact]</c> in the
/// <see cref="SessionBridgeServerCollection"/> collection deliberately,
/// unlike <see cref="RealExtensionLoadFixture"/>'s own reasons for doing
/// the same thing: <see cref="SessionBridgeServer.Start"/> binds
/// <see cref="Bridge.SessionBridgeProtocol.PipeName"/> with a fixed
/// <c>maxNumberOfServerInstances</c> (see that class's own remarks) --
/// calling it a second time from a second fixture instance would exceed
/// that cap and throw, so this has to be the one place in the whole test
/// assembly that calls it.
/// </summary>
public sealed class SessionBridgeServerFixture
{
    /// <summary>
    /// A single tamper whose behavior every <see cref="SessionBridgeServerTests"/>
    /// fact can rely on: echoes a marker request header into the response
    /// (provable proof a request actually reached this extension and its
    /// edit came back out), and rewrites any response body to a fixed,
    /// recognizable string.
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

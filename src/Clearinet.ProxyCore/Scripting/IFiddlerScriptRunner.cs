using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Scripting;

/// <summary>
/// The narrow surface <c>InterceptingProxyListener</c> needs in order to run
/// FiddlerScript's <c>Handlers.OnBeforeRequest</c>/<c>OnBeforeResponse</c>
/// against a real request/response pair, without <c>Clearinet.ProxyCore</c>
/// taking on a dependency on Jint or
/// <c>Clearinet.Compatibility.FiddlerScript.Exchange</c>.
///
/// This interface exists purely to avoid a circular project reference:
/// <c>Clearinet.Compatibility</c> already references <c>Clearinet.ProxyCore</c>
/// (for <see cref="CapturedRequest"/>/<see cref="CapturedResponse"/>), so
/// <c>InterceptingProxyListener</c> (which lives in <c>ProxyCore</c>) cannot
/// reference <c>FiddlerScriptRunner</c>/<c>FiddlerScriptHost</c> (which live
/// in <c>Compatibility</c>) directly. Defining this interface here and
/// implementing it there -- see
/// <c>Clearinet.Compatibility.FiddlerScript.FiddlerScriptRunner</c> -- lets
/// the dependency arrow point only one way, the same way it already does for
/// every other type in this file's namespace. A composition root
/// (<c>Clearinet.DesktopUi</c>'s <c>MainWindowViewModel</c>, today; any
/// future console host, later) is what actually constructs the real
/// implementation and hands it to <c>InterceptingProxyListener</c> as this
/// interface -- exactly the same pattern already used for
/// <c>BreakpointManager</c> and <c>AutoResponderRules</c>, just crossing an
/// assembly boundary this time since those two both already live in
/// <c>ProxyCore</c> itself.
///
/// See the FiddlerScript Compatibility Design doc's "Phase A2: wiring into
/// the listener" section for the full design this interface was drawn from,
/// including the scope this first pass deliberately leaves out: the script
/// never runs for a request AutoResponder answers locally (see
/// <c>InterceptingProxyListener.PumpSessionsAsync</c>'s own remarks on why),
/// <c>OnPeekAtResponseHeaders</c> stays unwired, and neither
/// <c>Exchange.hostname</c> retargeting nor <c>Exchange.bypassGateway</c>
/// feed back into routing (those are pre-existing gaps in <c>Exchange</c>
/// itself, unchanged by this interface).
/// </summary>
public interface IFiddlerScriptRunner
{
    /// <summary>
    /// Whether the currently-loaded script defines <c>Handlers.OnBeforeRequest</c>.
    /// The listener uses this to decide whether a request's body needs to be
    /// buffered at all before this runner would have anything to do with it
    /// -- the same "only buffer when something is actually going to act on
    /// it" fork <c>BreakpointManager</c> and AutoResponder's own force-flags
    /// already drive. An implementation is expected to cache this at
    /// load/reload time rather than re-evaluating it on every request; see
    /// <c>FiddlerScriptRunner</c>'s own remarks.
    /// </summary>
    bool HasOnBeforeRequest { get; }

    /// <summary>Same as <see cref="HasOnBeforeRequest"/>, for <c>Handlers.OnBeforeResponse</c>.</summary>
    bool HasOnBeforeResponse { get; }

    /// <summary>
    /// Runs <c>Handlers.OnBeforeRequest</c> against <paramref name="request"/>
    /// and returns the edited result. Callers only reach this once they've
    /// already decided to buffer the body (via <see cref="HasOnBeforeRequest"/>),
    /// but an implementation should still treat "no handler defined" as a
    /// harmless no-op rather than assuming the caller always checked first.
    /// A script error should not propagate out of this call and take the
    /// connection down with it -- see <c>FiddlerScriptRunner</c>'s own
    /// remarks on how it handles that.
    /// </summary>
    /// <param name="sessionOrdinal">
    /// A best-effort <c>oSession.id</c> -- see
    /// <c>Clearinet.ProxyCore.Sessions.SessionStore.PeekNextId</c>'s own
    /// remarks on why this is "best effort under concurrency," never a
    /// reservation.
    /// </param>
    /// <param name="hostname">The target hostname -- <c>oSession.hostname</c>.</param>
    FiddlerScriptRequestResult RunOnBeforeRequest(int sessionOrdinal, string hostname, CapturedRequest request);

    /// <summary>Same as <see cref="RunOnBeforeRequest"/>, for <c>Handlers.OnBeforeResponse</c>.</summary>
    FiddlerScriptResponseResult RunOnBeforeResponse(
        int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response);
}

/// <summary>
/// The edited request <see cref="IFiddlerScriptRunner.RunOnBeforeRequest"/>
/// hands back. Deliberately just the request itself -- an earlier version of
/// this design also considered a force-breakpoint flag here (mirroring
/// <c>AutoResponderOutcome.ForceBreakpointBeforeRequest</c>), but nothing
/// downstream would have consumed it beyond what buffering the body already
/// implies, so it was left out rather than added as dead surface. See the
/// design doc's own note on this.
/// </summary>
public sealed record FiddlerScriptRequestResult(CapturedRequest Request);

/// <summary>The edited response <see cref="IFiddlerScriptRunner.RunOnBeforeResponse"/> hands back. Same reasoning as <see cref="FiddlerScriptRequestResult"/> for why this carries only the response.</summary>
public sealed record FiddlerScriptResponseResult(CapturedResponse Response);

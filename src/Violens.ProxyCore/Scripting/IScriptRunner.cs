using Violens.ProxyCore.Http;

namespace Violens.ProxyCore.Scripting;

/// <summary>
/// A script that can change each request before it's sent and each
/// response before it's returned. VioLens's FiddlerScript support
/// implements it in <c>Violens.Compatibility</c>, which ProxyCore can't
/// reference. Scripts don't run for requests AutoResponder answers.
/// </summary>
public interface IScriptRunner
{
    /// <summary>
    /// Whether the script defines <c>OnBeforeRequest</c>; if so, request bodies
    /// are buffered. Implementations should cache this when the script loads.
    /// </summary>
    bool HasOnBeforeRequest { get; }

    /// <summary>Whether the script defines <c>OnBeforeResponse</c>.</summary>
    bool HasOnBeforeResponse { get; }

    /// <summary>
    /// Runs <c>OnBeforeRequest</c> and returns the edited request. Does nothing
    /// if there's no handler, and never lets a script error escape.
    /// </summary>
    /// <param name="sessionOrdinal">The likely <c>oSession.id</c> (see <c>SessionStore.PeekNextId</c>).</param>
    /// <param name="hostname"><c>oSession.hostname</c>.</param>
    ScriptRequestResult RunOnBeforeRequest(int sessionOrdinal, string hostname, CapturedRequest request);

    /// <summary>Runs <c>OnBeforeResponse</c> and returns the edited response.</summary>
    ScriptResponseResult RunOnBeforeResponse(
        int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response);
}

/// <summary>The request after <see cref="IScriptRunner.RunOnBeforeRequest"/>.</summary>
public sealed record ScriptRequestResult(CapturedRequest Request);

/// <summary>The response after <see cref="IScriptRunner.RunOnBeforeResponse"/>.</summary>
public sealed record ScriptResponseResult(CapturedResponse Response);

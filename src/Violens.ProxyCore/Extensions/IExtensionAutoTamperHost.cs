using Violens.ProxyCore.Http;

namespace Violens.ProxyCore.Extensions;

/// <summary>
/// Runs every loaded extension's <c>IAutoTamper</c> hooks against live
/// traffic, in load order, each seeing the previous one's edits. An
/// interface because ProxyCore can't reference <c>Violens.Compatibility</c>.
/// FiddlerScript's handlers run before these.
/// </summary>
public interface IExtensionAutoTamperHost
{
    /// <summary>Whether any extension has a request hook; if so, request bodies are buffered.</summary>
    bool HasAnyRequestBeforeHandlers { get; }

    /// <summary>Whether any extension has a response hook; if so, response bodies are buffered.</summary>
    bool HasAnyResponseBeforeHandlers { get; }

    /// <summary>Runs every <c>AutoTamperRequestBefore</c> in turn and returns the edited request.</summary>
    CapturedRequest RunRequestBefore(int sessionOrdinal, string hostname, CapturedRequest request);

    /// <summary>Runs every <c>AutoTamperRequestAfter</c>. The request has already been sent.</summary>
    void RunRequestAfter(int sessionOrdinal, string hostname, CapturedRequest request);

    /// <summary>Runs every <c>AutoTamperResponseBefore</c> in turn and returns the edited response.</summary>
    CapturedResponse RunResponseBefore(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response);

    /// <summary>Runs every <c>AutoTamperResponseAfter</c>.</summary>
    void RunResponseAfter(int sessionOrdinal, string hostname, CapturedRequest request, CapturedResponse response);
}

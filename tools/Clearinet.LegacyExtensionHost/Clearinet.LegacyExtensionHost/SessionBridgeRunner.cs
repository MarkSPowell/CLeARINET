using System;
using System.Collections.Generic;
using Clearinet.CompatShim;
using Clearinet.LegacyExtensionHost.Bridge;

namespace Clearinet.LegacyExtensionHost;

/// <summary>
/// Handles one <see cref="BridgeRequestMessage"/> against every loaded
/// extension that implements <see cref="IAutoTamper"/> -- the legacy
/// host's own mirror of
/// <c>Clearinet.Compatibility.Extensions.LoadedExtensionSet</c> (see that
/// class's own remarks for the shared shape: one <see cref="Session"/>
/// built per call, every tamper's hook run against that SAME instance in
/// sequence so each one sees the previous one's edits, exactly like real
/// Fiddler running several loaded extensions against one shared
/// <c>oSession</c>), adapted for the out-of-process case -- the "shared
/// instance" is scoped to a single bridge call rather than living for a
/// whole in-process request, and the final state is read back out as a
/// wire message instead of an immutable <c>CapturedRequest</c>/<c>CapturedResponse</c>
/// record.
///
/// Deliberately takes no dependency on named pipes or
/// <see cref="SessionBridgeProtocol"/> at all -- <see cref="SessionBridgeServer"/>
/// owns the actual pipe I/O and hands this class one already-deserialized
/// message at a time, so the request/response handling logic here is
/// testable without a real pipe connection (see
/// <c>SessionBridgeRunnerTests</c>).
/// </summary>
internal sealed class SessionBridgeRunner
{
    private readonly IReadOnlyList<IAutoTamper> _tampers;
    private readonly Action<string> _log;

    public SessionBridgeRunner(IReadOnlyList<IAutoTamper> tampers, Action<string> log)
    {
        _tampers = tampers ?? throw new ArgumentNullException(nameof(tampers));
        _log = log ?? (_ => { });
    }

    public BridgeResponseMessage Handle(BridgeRequestMessage message)
    {
        switch (message.Kind)
        {
            case BridgeMessageKind.Capabilities:
                return new BridgeResponseMessage { Ok = true, AutoTamperCount = _tampers.Count };

            case BridgeMessageKind.RequestBefore:
                return RunRequestHook(message, nameof(IAutoTamper.AutoTamperRequestBefore), (tamper, session) => tamper.AutoTamperRequestBefore(session));

            case BridgeMessageKind.RequestAfter:
                return RunRequestHook(message, nameof(IAutoTamper.AutoTamperRequestAfter), (tamper, session) => tamper.AutoTamperRequestAfter(session));

            case BridgeMessageKind.ResponseBefore:
                return RunResponseHook(message, nameof(IAutoTamper.AutoTamperResponseBefore), (tamper, session) => tamper.AutoTamperResponseBefore(session));

            case BridgeMessageKind.ResponseAfter:
                return RunResponseHook(message, nameof(IAutoTamper.AutoTamperResponseAfter), (tamper, session) => tamper.AutoTamperResponseAfter(session));

            default:
                return new BridgeResponseMessage { Ok = false, Error = $"Unknown session bridge message kind: {message.Kind}." };
        }
    }

    private BridgeResponseMessage RunRequestHook(BridgeRequestMessage message, string hookName, Action<IAutoTamper, Session> hook)
    {
        if (message.Request is null)
        {
            return new BridgeResponseMessage { Ok = false, Error = $"{hookName}: message carried no Request payload." };
        }

        var session = SessionMapping.ToRequestSession(message.SessionOrdinal, message.Hostname ?? string.Empty, message.Request, _log);
        RunEveryTamper(hookName, tamper => hook(tamper, session));

        return new BridgeResponseMessage
        {
            Ok = true,
            Request = SessionMapping.FromSession(session, message.Request),
        };
    }

    private BridgeResponseMessage RunResponseHook(BridgeRequestMessage message, string hookName, Action<IAutoTamper, Session> hook)
    {
        if (message.Request is null || message.Response is null)
        {
            return new BridgeResponseMessage { Ok = false, Error = $"{hookName}: message carried no Request/Response payload." };
        }

        var session = SessionMapping.ToResponseSession(message.SessionOrdinal, message.Hostname ?? string.Empty, message.Request, message.Response, _log);
        RunEveryTamper(hookName, tamper => hook(tamper, session));

        return new BridgeResponseMessage
        {
            Ok = true,
            Response = SessionMapping.FromSession(session, message.Response),
        };
    }

    /// <summary>
    /// Isolates one extension's throw from the others -- identical
    /// reasoning and behavior to <c>LoadedExtensionSet.RunSafely</c> (see
    /// that method's own remarks): a badly-behaved loaded extension
    /// shouldn't take the others, or this bridge call, down with it. Logged
    /// through the same <see cref="Action{T}"/> <see cref="Program"/> wires
    /// to both the Log tab and <c>Debug.WriteLine</c>.
    /// </summary>
    private void RunEveryTamper(string hookName, Action<IAutoTamper> run)
    {
        foreach (var tamper in _tampers)
        {
            try
            {
                run(tamper);
            }
            catch (Exception ex)
            {
                _log($"[SessionBridge] {tamper.GetType().FullName}.{hookName} threw: {ex.Message}");
            }
        }
    }
}

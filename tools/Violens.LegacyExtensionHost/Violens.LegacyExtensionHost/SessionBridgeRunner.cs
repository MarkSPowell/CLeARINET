using System;
using System.Collections.Generic;
using Violens.CompatShim;
using Violens.LegacyExtensionHost.Bridge;

namespace Violens.LegacyExtensionHost;

/// <summary>
/// Handles one bridge message: builds a <see cref="Session"/>, runs every
/// IAutoTamper on it in turn so each sees the previous one's edits, and
/// returns the result. No pipe code, so it's testable on its own.
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

    /// <summary>Runs each extension, logging and skipping any that throw.</summary>
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

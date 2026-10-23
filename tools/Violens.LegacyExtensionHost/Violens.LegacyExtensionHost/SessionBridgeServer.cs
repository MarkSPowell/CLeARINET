using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using Violens.CompatShim;
using Violens.LegacyExtensionHost.Bridge;

namespace Violens.LegacyExtensionHost;

/// <summary>
/// Listens on the bridge pipe and answers each call with
/// <see cref="SessionBridgeRunner"/>. Started from <see cref="Program.Main"/>
/// before the window runs. Background threads with no shutdown: they end
/// with the process.
/// </summary>
internal static class SessionBridgeServer
{
    /// <summary>Pipe instances listening at once, so one slow request doesn't hold up another.</summary>
    private const int InstanceCount = 4;

    public static void Start(IReadOnlyList<IAutoTamper> tampers, Action<string> log)
    {
        var runner = new SessionBridgeRunner(tampers, log);

        for (var i = 0; i < InstanceCount; i++)
        {
            var instanceNumber = i;
            var thread = new Thread(() => ListenLoop(runner, instanceNumber, log))
            {
                IsBackground = true,
                Name = $"SessionBridgeServer-{instanceNumber}",
            };
            thread.Start();
        }

        log($"[SessionBridge] Listening on pipe \"{SessionBridgeProtocol.PipeName}\" ({InstanceCount} concurrent instances, {tampers.Count} AutoTamper extension(s) loaded).");
    }

    /// <summary>One instance's loop: accept a connection, handle one call, disconnect, repeat.</summary>
    private static void ListenLoop(SessionBridgeRunner runner, int instanceNumber, Action<string> log)
    {
        // One server instance per thread, reused with Disconnect() and
        // WaitForConnection(). Creating a new one per call let clients
        // occasionally connect to an instance being torn down
        // (EndOfStreamException in the tests).
        NamedPipeServerStream server = null;

        while (true)
        {
            try
            {
                server ??= new NamedPipeServerStream(
                    SessionBridgeProtocol.PipeName,
                    PipeDirection.InOut,
                    InstanceCount,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None,
                    SessionBridgeProtocol.PipeBufferSize,
                    SessionBridgeProtocol.PipeBufferSize);

                server.WaitForConnection();

                try
                {
                    var request = SessionBridgeProtocol.ReadMessage<BridgeRequestMessage>(server);
                    var response = runner.Handle(request);
                    SessionBridgeProtocol.WriteMessage(server, response);

                    // Wait for the client to read everything before disconnecting, which
                    // would otherwise discard unread bytes.
                    server.WaitForPipeDrain();
                }
                catch (Exception ex)
                {
                    // A client failing mid-call (the app exiting, say) doesn't stop this
                    // listener.
                    log($"[SessionBridge] Instance {instanceNumber}: connection failed mid-exchange: {ex.Message}");
                }
                finally
                {
                    if (server.IsConnected)
                    {
                        server.Disconnect();
                    }
                }
            }
            catch (Exception ex)
            {
                // The instance itself failed: replace it and retry after a short pause.
                server?.Dispose();
                server = null;
                log($"[SessionBridge] Instance {instanceNumber}: listener error: {ex.Message}");
                Thread.Sleep(1000);
            }
        }
    }
}

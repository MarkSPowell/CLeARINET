using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using Clearinet.CompatShim;
using Clearinet.LegacyExtensionHost.Bridge;

namespace Clearinet.LegacyExtensionHost;

/// <summary>
/// The legacy host's side of the session bridge (see the design doc's
/// "Session bridge" section): listens on
/// <see cref="SessionBridgeProtocol.PipeName"/> for calls from the main
/// app's <c>Clearinet.Compatibility.Extensions.LegacyExtensionHostBridgeClient</c>,
/// and answers each one via <see cref="SessionBridgeRunner"/>. Started once
/// from <see cref="Program.Main"/>, right after <c>LegacyExtensionLoader.Load()</c>
/// -- see that method's own remarks on why this needs to be running before
/// <c>Application.Run(mainForm)</c> blocks the main thread.
///
/// Runs entirely on background threads (<see cref="Thread.IsBackground"/>
/// <c>true</c>), deliberately with no explicit shutdown path: when this
/// process exits, the OS tears these down with it -- simpler and just as
/// correct as adding a <see cref="CancellationTokenSource"/> a blocked,
/// synchronous <see cref="NamedPipeServerStream.WaitForConnection"/> can't
/// cooperatively observe anyway.
/// </summary>
internal static class SessionBridgeServer
{
    /// <summary>
    /// How many <see cref="NamedPipeServerStream"/> instances of the same
    /// pipe name listen concurrently -- Windows named pipes support several
    /// server instances under one name out of the box (each
    /// <c>maxNumberOfServerInstances</c>-aware), so a handful of loaded
    /// extensions doing real work for one live request don't force a second,
    /// concurrent request (e.g. from a different browser tab) to queue
    /// behind it. Not meant to scale arbitrarily -- see this project's own
    /// README for the honest scope of what "real" concurrency this bridge
    /// has actually been exercised against.
    /// </summary>
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

    /// <summary>
    /// One instance's own accept loop: waits for a connection, handles
    /// exactly one request/response exchange on it, disconnects, and waits
    /// again -- matching this bridge client's own one-pipe-connection-per-call
    /// model (see <c>LegacyExtensionHostBridgeClient</c>'s own remarks for
    /// why that shape was chosen over one long-lived connection).
    /// </summary>
    private static void ListenLoop(SessionBridgeRunner runner, int instanceNumber, Action<string> log)
    {
        // One NamedPipeServerStream instance is created here and then
        // REUSED for every call this thread ever handles: Disconnect()
        // followed by WaitForConnection() again on the SAME instance is
        // the documented, intended way to accept the next client (see
        // NamedPipeServerStream.Disconnect's own remarks -- "you can call
        // WaitForConnection again"). Earlier this loop instead Disposed
        // the instance and constructed a brand new one on every single
        // call. That meant, with 4 threads all doing the same thing, the
        // OS-level pipe instance backing this thread's slot was being torn
        // down and rebuilt constantly, and an incoming client connection
        // could occasionally get handed to an instance that was
        // mid-teardown rather than one actually sitting in
        // WaitForConnection -- SessionBridgeServerTests caught this as an
        // intermittent EndOfStreamException on the client, reproducible
        // enough to fail a handful of real calls out of every few dozen.
        // Creating the instance once per thread and only ever recreating
        // it after a genuine listener-level failure (the outer catch
        // below) removes that churn, and with it the race.
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

                    // WriteMessage only guarantees the bytes have been
                    // copied into the pipe's kernel-side buffer, not that
                    // the client has actually read them yet -- Disconnect()
                    // below tears the connection down immediately and can
                    // discard whatever the client hasn't read out of that
                    // buffer yet. WaitForPipeDrain blocks until the client
                    // has read every byte this call wrote, so the
                    // Disconnect() below can never race a still-in-flight
                    // response. Kept as defense in depth alongside the
                    // instance-reuse fix above -- harmless once the client
                    // really has finished reading, and cheap insurance
                    // against the same class of race resurfacing.
                    server.WaitForPipeDrain();
                }
                catch (Exception ex)
                {
                    // One misbehaving client (e.g. the main app's process
                    // exiting mid-exchange) shouldn't take this listener
                    // instance down -- log and go back to
                    // WaitForConnection (on this same, still-good instance)
                    // for the next caller.
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
                // Something went wrong with the instance itself -- before
                // or during accepting a connection, not a per-call failure
                // (see above). Dispose it so the next loop iteration builds
                // a genuinely fresh one instead of retrying a possibly
                // broken instance, log it, and retry rather than letting
                // this instance's whole accept loop die silently, with a
                // short pause so a persistent problem (e.g. the pipe name
                // somehow already held elsewhere) doesn't spin the CPU.
                server?.Dispose();
                server = null;
                log($"[SessionBridge] Instance {instanceNumber}: listener error: {ex.Message}");
                Thread.Sleep(1000);
            }
        }
    }
}

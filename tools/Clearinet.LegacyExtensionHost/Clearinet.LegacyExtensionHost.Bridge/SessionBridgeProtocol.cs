using System.IO;
using System.Text;
using System.Text.Json;

namespace Clearinet.LegacyExtensionHost.Bridge;

/// <summary>
/// Shared constants and message framing for the session bridge's named
/// pipe -- one Windows local pipe, no network exposure (see the design
/// doc's "Session bridge" section for why named pipes were chosen over
/// any networked transport: this never needs to cross a machine boundary,
/// and a named pipe can't be reached from another machine at all, which
/// matters for a tool that's inspecting the user's own HTTPS traffic).
/// Used identically by the main app's client
/// (<c>Clearinet.Compatibility.Extensions.LegacyExtensionHostBridgeClient</c>)
/// and the legacy host's server (<c>Clearinet.LegacyExtensionHost.SessionBridgeServer</c>)
/// -- framing has to match exactly on both ends, so it lives here once
/// rather than being reimplemented twice.
/// </summary>
public static class SessionBridgeProtocol
{
    /// <summary>
    /// The pipe name both ends connect to. Versioned in the name itself
    /// (rather than a separate handshake field) so a future,
    /// wire-incompatible protocol change just fails to connect instead of
    /// connecting and then misbehaving -- simplest possible compatibility
    /// guard for two binaries (net10.0 main app, net48 legacy host) that
    /// are never guaranteed to be rebuilt and redeployed together.
    /// </summary>
    public const string PipeName = "Clearinet.LegacyExtensionHost.SessionBridge.v1";

    /// <summary>
    /// How long the client waits to even connect before deciding no legacy
    /// host is listening. Short and local-only (this is loopback IPC, not a
    /// network call) -- see <c>LegacyExtensionHostBridgeClient</c>'s own
    /// remarks for why a slow connect has to fail fast rather than block
    /// real browsing traffic.
    /// </summary>
    public const int ConnectTimeoutMilliseconds = 250;

    /// <summary>
    /// How long the client waits for a full request/response round trip
    /// once connected, covering however long every loaded legacy
    /// extension's hook actually takes to run. Generous compared to
    /// <see cref="ConnectTimeoutMilliseconds"/> on purpose: a slow but
    /// working extension should get to finish; a hung one should still
    /// eventually let real browsing traffic through rather than stall it
    /// forever -- see <c>LegacyExtensionHostBridgeClient.Exchange</c>.
    /// </summary>
    public const int RoundTripTimeoutMilliseconds = 3000;

    /// <summary>
    /// The pipe's own read/write buffer size (bytes) -- not a hard cap on
    /// message size (<see cref="WriteMessage{T}"/>/<see cref="ReadMessage{T}"/>
    /// length-prefix and loop until a full message is transferred either
    /// way), just the chunk size each individual OS-level read/write call
    /// uses.
    /// </summary>
    public const int PipeBufferSize = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Writes one length-prefixed JSON message: a 4-byte little-endian
    /// byte count, then that many UTF-8 JSON bytes. Symmetric with
    /// <see cref="ReadMessage{T}"/> -- same framing is used in both
    /// directions (request main app -> legacy host, response the other
    /// way), so this one method serves both ends.
    /// </summary>
    public static void WriteMessage<T>(Stream stream, T message)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        var lengthPrefix = System.BitConverter.GetBytes(payload.Length);
        if (!System.BitConverter.IsLittleEndian)
        {
            System.Array.Reverse(lengthPrefix);
        }

        stream.Write(lengthPrefix, 0, lengthPrefix.Length);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    /// <summary>
    /// Reads one length-prefixed JSON message written by
    /// <see cref="WriteMessage{T}"/>. Loops on both the 4-byte length
    /// prefix and the payload itself, since <see cref="Stream.Read"/> --
    /// including on a <see cref="System.IO.Pipes.PipeStream"/> -- is only
    /// ever guaranteed to return "at least one byte, up to what was asked
    /// for," never "exactly what was asked for" in one call.
    /// </summary>
    public static T ReadMessage<T>(Stream stream)
    {
        var lengthPrefix = ReadExactly(stream, 4);
        if (!System.BitConverter.IsLittleEndian)
        {
            System.Array.Reverse(lengthPrefix);
        }

        var length = System.BitConverter.ToInt32(lengthPrefix, 0);
        if (length < 0 || length > 64 * 1024 * 1024)
        {
            // A real message from either end of this protocol is never
            // remotely this large (an inspected HTTP body would have to be
            // enormous) -- rejecting outright here guards against reading a
            // garbled/out-of-sync stream as if it were a legitimate,
            // absurdly large allocation request.
            throw new InvalidDataException($"Session bridge message length {length} is out of the accepted range.");
        }

        var payload = ReadExactly(stream, length);
        var result = JsonSerializer.Deserialize<T>(payload, SerializerOptions);
        return result ?? throw new InvalidDataException("Session bridge message deserialized to null.");
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Session bridge pipe closed before a full message was received.");
            }

            offset += read;
        }

        return buffer;
    }
}

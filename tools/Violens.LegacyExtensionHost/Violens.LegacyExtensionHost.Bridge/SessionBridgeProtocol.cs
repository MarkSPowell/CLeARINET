using System.IO;
using System.Text;
using System.Text.Json;

namespace Violens.LegacyExtensionHost.Bridge;

/// <summary>
/// Pipe name, timeouts and framing for the session bridge, shared by both
/// ends. A local named pipe, which other machines can't reach.
/// </summary>
public static class SessionBridgeProtocol
{
    /// <summary>
    /// Versioned in the name, so an incompatible build fails to connect
    /// rather than misbehaving; the two ends aren't always updated together.
    /// </summary>
    public const string PipeName = "Violens.LegacyExtensionHost.SessionBridge.v1";

    /// <summary>How long to try connecting before deciding no host is there. Short, so traffic isn't held up.</summary>
    public const int ConnectTimeoutMilliseconds = 250;

    /// <summary>
    /// How long a round trip may take, including the extensions' hooks. Long
    /// enough for a slow extension; a hung one still lets traffic through.
    /// </summary>
    public const int RoundTripTimeoutMilliseconds = 3000;

    /// <summary>The pipe's buffer size; messages can be larger.</summary>
    public const int PipeBufferSize = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>Writes one message: a 4-byte little-endian length, then UTF-8 JSON.</summary>
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

    /// <summary>Reads one message, looping because a read may return fewer bytes than asked for.</summary>
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

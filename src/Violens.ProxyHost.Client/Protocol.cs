using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Violens.ProxyHost.Client
{
    /// <summary>
    /// The messages between an app (through <see cref="ProxyHostClient"/>) and
    /// the Violens.ProxyHost process, over one named pipe in both directions.
    /// The host compiles this same file (through InternalsVisibleTo), so the
    /// two sides can't disagree about the format.
    ///
    /// Each message is a frame: a 4-byte little-endian length, then that many
    /// bytes, the first being the <see cref="MessageType"/> and the rest its
    /// fields written with <see cref="BinaryWriter"/>. Commands from the app
    /// start with a request id, echoed in the host's reply.
    /// </summary>
    internal static class Protocol
    {
        /// <summary>Bumped whenever a message's fields change. The two sides refuse to talk across versions.</summary>
        public const int Version = 1;

        /// <summary>No frame is larger than this (a captured body can be big, but not this big).</summary>
        public const int MaxFrameBytes = 512 * 1024 * 1024;

        private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static void WriteFrame(Stream stream, object writeLock, MessageType type, Action<BinaryWriter> writeFields)
        {
            byte[] payload;
            using (var buffer = new MemoryStream())
            {
                using (var writer = new BinaryWriter(buffer, Utf8, leaveOpen: true))
                {
                    writer.Write((byte)type);
                    writeFields(writer);
                }

                payload = buffer.ToArray();
            }

            var length = BitConverter.GetBytes(payload.Length);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(length);
            }

            lock (writeLock)
            {
                stream.Write(length, 0, 4);
                stream.Write(payload, 0, payload.Length);
                stream.Flush();
            }
        }

        /// <summary>
        /// Reads one frame. Returns false at the end of the stream (the other
        /// side closed the pipe). Blocks until a whole frame has arrived.
        /// </summary>
        public static bool TryReadFrame(Stream stream, out MessageType type, out BinaryReader fields)
        {
            type = default;
            fields = null!;

            var lengthBytes = new byte[4];
            if (!ReadExactly(stream, lengthBytes, allowEndOfStream: true))
            {
                return false;
            }

            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(lengthBytes);
            }

            var length = BitConverter.ToInt32(lengthBytes, 0);
            if (length < 1 || length > MaxFrameBytes)
            {
                throw new InvalidDataException($"A message of {length} bytes isn't valid.");
            }

            var payload = new byte[length];
            ReadExactly(stream, payload, allowEndOfStream: false);
            type = (MessageType)payload[0];
            fields = new BinaryReader(new MemoryStream(payload, 1, length - 1, writable: false), Utf8);
            return true;
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, bool allowEndOfStream)
        {
            var read = 0;
            while (read < buffer.Length)
            {
                var count = stream.Read(buffer, read, buffer.Length - read);
                if (count == 0)
                {
                    if (read == 0 && allowEndOfStream)
                    {
                        return false;
                    }

                    throw new EndOfStreamException("The pipe closed in the middle of a message.");
                }

                read += count;
            }

            return true;
        }

        // --- Field helpers ---------------------------------------------------

        public static void WriteNullableString(this BinaryWriter writer, string? value)
        {
            writer.Write(value != null);
            if (value != null)
            {
                writer.Write(value);
            }
        }

        public static string? ReadNullableString(this BinaryReader reader) =>
            reader.ReadBoolean() ? reader.ReadString() : null;

        public static void WriteBytes(this BinaryWriter writer, byte[] value)
        {
            writer.Write(value.Length);
            writer.Write(value);
        }

        public static byte[] ReadBytes(this BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > MaxFrameBytes)
            {
                throw new InvalidDataException($"A byte field of {length} bytes isn't valid.");
            }

            var bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new EndOfStreamException("A message ended in the middle of a byte field.");
            }

            return bytes;
        }

        public static void WriteHeaders(this BinaryWriter writer, List<KeyValuePair<string, string>> headers)
        {
            writer.Write(headers.Count);
            foreach (var header in headers)
            {
                writer.Write(header.Key);
                writer.Write(header.Value);
            }
        }

        public static List<KeyValuePair<string, string>> ReadHeaders(this BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var headers = new List<KeyValuePair<string, string>>(Math.Max(0, Math.Min(count, 1024)));
            for (var i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                var value = reader.ReadString();
                headers.Add(new KeyValuePair<string, string>(name, value));
            }

            return headers;
        }

        public static void WriteFlags(this BinaryWriter writer, IDictionary<string, string> flags)
        {
            writer.Write(flags.Count);
            foreach (var flag in flags)
            {
                writer.Write(flag.Key);
                writer.Write(flag.Value);
            }
        }

        public static Dictionary<string, string> ReadFlags(this BinaryReader reader)
        {
            var count = reader.ReadInt32();
            var flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                flags[name] = reader.ReadString();
            }

            return flags;
        }

        public static void WriteRequest(this BinaryWriter writer, ProxyRequest request)
        {
            writer.Write(request.Method);
            writer.Write(request.Target);
            writer.Write(request.HttpVersion);
            writer.WriteHeaders(request.Headers);
            writer.WriteBytes(request.Body);
        }

        public static ProxyRequest ReadRequest(this BinaryReader reader)
        {
            var request = new ProxyRequest
            {
                Method = reader.ReadString(),
                Target = reader.ReadString(),
                HttpVersion = reader.ReadString(),
            };
            request.Headers = reader.ReadHeaders();
            request.Body = reader.ReadBytes();
            return request;
        }

        public static void WriteResponse(this BinaryWriter writer, ProxyResponse response)
        {
            writer.Write(response.HttpVersion);
            writer.Write(response.StatusCode);
            writer.Write(response.ReasonPhrase);
            writer.WriteHeaders(response.Headers);
            writer.WriteBytes(response.Body);
        }

        public static ProxyResponse ReadResponse(this BinaryReader reader)
        {
            var response = new ProxyResponse
            {
                HttpVersion = reader.ReadString(),
                StatusCode = reader.ReadInt32(),
                ReasonPhrase = reader.ReadString(),
            };
            response.Headers = reader.ReadHeaders();
            response.Body = reader.ReadBytes();
            return response;
        }

        public static void WriteNullableResponse(this BinaryWriter writer, ProxyResponse? response)
        {
            writer.Write(response != null);
            if (response != null)
            {
                writer.WriteResponse(response);
            }
        }

        public static ProxyResponse? ReadNullableResponse(this BinaryReader reader) =>
            reader.ReadBoolean() ? reader.ReadResponse() : null;

        public static void WriteSession(this BinaryWriter writer, CapturedSession session)
        {
            writer.Write(session.Id);
            writer.Write(session.Host);
            writer.Write(session.Scheme);
            writer.Write(session.Url);
            writer.Write(session.StartedAt.ToUnixTimeMilliseconds());
            writer.WriteRequest(session.Request);
            writer.WriteResponse(session.Response);
            writer.WriteFlags(session.Flags);
        }

        public static CapturedSession ReadSession(this BinaryReader reader)
        {
            var session = new CapturedSession
            {
                Id = reader.ReadInt32(),
                Host = reader.ReadString(),
                Scheme = reader.ReadString(),
                Url = reader.ReadString(),
                StartedAt = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()),
            };
            session.Request = reader.ReadRequest();
            session.Response = reader.ReadResponse();
            session.Flags = reader.ReadFlags();
            return session;
        }
    }

    /// <summary>
    /// What a frame carries. "App" messages go from the app to the host;
    /// "Host" messages the other way. Never renumber: add new ones at the end
    /// and bump <see cref="Protocol.Version"/>.
    /// </summary>
    internal enum MessageType : byte
    {
        /// <summary>Host, first: protocol version (int), the launch token (string), host version (string).</summary>
        HostHello = 1,

        /// <summary>App: request id, then the identity and <see cref="StartOptions"/> fields (see ProxyHostClient.StartAsync).</summary>
        Start = 2,

        /// <summary>Host: request id, port (int), root certificate (bytes), upstream description (nullable string).</summary>
        Started = 3,

        /// <summary>App: request id. Stops capturing and puts the system proxy back.</summary>
        Stop = 4,

        /// <summary>Host: request id. A command succeeded.</summary>
        Ok = 5,

        /// <summary>Host: request id, message. A command failed.</summary>
        Error = 6,

        /// <summary>App: request id, then the <see cref="BreakpointSettings"/> fields.</summary>
        SetBreakpoints = 7,

        /// <summary>Host: hook id (long), stage (byte), session id, host, scheme, request, response (nullable), flags.</summary>
        HookRequest = 8,

        /// <summary>App: hook id, request, response (nullable), local response (nullable), flags.</summary>
        HookReply = 9,

        /// <summary>Host: breakpoint id (long), session ordinal, stage (byte), host, raw text.</summary>
        BreakpointHit = 10,

        /// <summary>App: breakpoint id, resume (bool), edited raw text (nullable string).</summary>
        BreakpointReply = 11,

        /// <summary>Host: a captured session.</summary>
        SessionCaptured = 12,

        /// <summary>Host: time (Unix milliseconds), category, message.</summary>
        Log = 13,

        /// <summary>App: request id, path. Saves every captured session as a SAZ file.</summary>
        SaveSaz = 14,

        /// <summary>App: request id, path. Opens a SAZ file; its sessions arrive as SessionCaptured messages.</summary>
        LoadSaz = 15,

        /// <summary>Host: request id, count (int). The reply to SaveSaz and LoadSaz.</summary>
        Count = 16,

        /// <summary>App: request id. Forgets every captured session.</summary>
        ClearSessions = 17,

        /// <summary>App: request id. Removes the root certificate from the user's certificate stores.</summary>
        UninstallRootCertificate = 18,
    }
}

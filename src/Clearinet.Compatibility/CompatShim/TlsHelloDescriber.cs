using System.Text;

namespace Clearinet.CompatShim;

/// <summary>
/// Backs <see cref="Utilities.UNSTABLE_DescribeClientHello"/> and
/// <see cref="Utilities.UNSTABLE_DescribeServerHello"/>. Written from the
/// TLS specifications (RFC 8446, RFC 5246 and the IANA TLS registries), not
/// from Fiddler: Fiddler's own output format isn't documented, so this one
/// is CLeARINET's.
///
/// <b>The one format rule callers rely on.</b> Eric Lawrence's NetLog
/// importer (public source) drops the first two non-empty lines of this
/// text and then looks for <c>supported_versions\tTls1.3</c> to spot a TLS
/// 1.3 ServerHello. So the output always starts with two header lines, and
/// each extension is one line of the form <c>\t&lt;name&gt;\t&lt;details&gt;</c>.
///
/// The 5-byte TLS record header in front of the message is skipped and its
/// length field ignored: that same importer builds a placeholder record
/// header with a made-up length. The handshake message's own length is used
/// instead.
/// </summary>
internal static class TlsHelloDescriber
{
    public static string Describe(MemoryStream stream, int expectedHandshakeType)
    {
        var kind = expectedHandshakeType == 1 ? "ClientHello" : "ServerHello";
        var sb = new StringBuilder();
        sb.Append("A TLS ").Append(kind).Append(" handshake message was found.\n");
        sb.Append("(Described by CLeARINET; the layout differs from Fiddler's.)\n");

        byte[] data;
        try
        {
            data = stream.ToArray()[(int)Math.Min(stream.Position, stream.Length)..];
        }
        catch (Exception ex)
        {
            return sb.Append("Unable to read the message: ").Append(ex.Message).Append('\n').ToString();
        }

        try
        {
            var reader = new Reader(data);
            reader.Skip(5); // record header: type, version, length (ignored -- see remarks)

            var handshakeType = reader.U8();
            if (handshakeType != expectedHandshakeType)
            {
                return sb.Append($"Unexpected handshake type {handshakeType} (expected {expectedHandshakeType}).\n").ToString();
            }

            var body = reader.Sub(reader.U24());
            var legacyVersion = body.U16();
            sb.Append($"Version: {VersionName(legacyVersion)}\n");
            sb.Append($"Random: {Hex(body.Bytes(32))}\n");
            var sessionId = body.Bytes(body.U8());
            sb.Append($"SessionID: {(sessionId.Length == 0 ? "empty" : Hex(sessionId))}\n");

            if (expectedHandshakeType == 1)
            {
                var suites = body.Sub(body.U16());
                sb.Append("Ciphers:\n");
                while (!suites.AtEnd)
                {
                    var suite = suites.U16();
                    sb.Append($"\t[{suite:X4}]\t{CipherSuiteName(suite)}\n");
                }

                var compression = body.Bytes(body.U8());
                sb.Append($"Compression: {(compression.Length == 1 && compression[0] == 0 ? "none" : Hex(compression))}\n");
            }
            else
            {
                var suite = body.U16();
                sb.Append($"Cipher: [{suite:X4}]\t{CipherSuiteName(suite)}\n");
                var compression = body.U8();
                sb.Append($"Compression: {(compression == 0 ? "none" : compression.ToString())}\n");
            }

            if (body.AtEnd)
            {
                return sb.Append("Extensions: none\n").ToString();
            }

            var extensions = body.Sub(body.U16());
            sb.Append("Extensions:\n");
            while (!extensions.AtEnd)
            {
                var type = extensions.U16();
                var extensionData = extensions.Sub(extensions.U16());
                sb.Append('\t').Append(ExtensionName(type)).Append('\t')
                  .Append(DescribeExtension(type, extensionData, isClientHello: expectedHandshakeType == 1))
                  .Append('\n');
            }

            return sb.ToString();
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
        {
            return sb.Append("The message is truncated or malformed; showing what was parsed above.\n").ToString();
        }
    }

    private static string DescribeExtension(int type, Reader data, bool isClientHello)
    {
        try
        {
            switch (type)
            {
                case 0x0000 when !data.AtEnd: // server_name
                {
                    var list = data.Sub(data.U16());
                    var names = new List<string>();
                    while (!list.AtEnd)
                    {
                        var nameType = list.U8();
                        var name = list.Bytes(list.U16());
                        names.Add(nameType == 0 ? Encoding.ASCII.GetString(name) : $"(type {nameType})");
                    }

                    return string.Join(", ", names);
                }

                case 0x0010: // application_layer_protocol_negotiation
                {
                    var list = data.Sub(data.U16());
                    var protocols = new List<string>();
                    while (!list.AtEnd)
                    {
                        protocols.Add(Encoding.ASCII.GetString(list.Bytes(list.U8())));
                    }

                    return string.Join(", ", protocols);
                }

                case 0x002B when isClientHello: // supported_versions (list)
                {
                    var list = data.Sub(data.U8());
                    var versions = new List<string>();
                    while (!list.AtEnd)
                    {
                        versions.Add(VersionName(list.U16()));
                    }

                    return string.Join(", ", versions);
                }

                case 0x002B: // supported_versions (the server's single choice)
                    return VersionName(data.U16());

                case 0x000A when isClientHello: // supported_groups
                {
                    var list = data.Sub(data.U16());
                    var groups = new List<string>();
                    while (!list.AtEnd)
                    {
                        groups.Add(GroupName(list.U16()));
                    }

                    return string.Join(", ", groups);
                }

                default:
                    return data.AtEnd ? "empty" : $"{data.Remaining} bytes";
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or InvalidOperationException)
        {
            return "malformed";
        }
    }

    /// <summary><c>Tls1.3</c> style, matching the token the NetLog importer searches for (see remarks).</summary>
    private static string VersionName(int version) => version switch
    {
        0x0300 => "Ssl3.0",
        0x0301 => "Tls1.0",
        0x0302 => "Tls1.1",
        0x0303 => "Tls1.2",
        0x0304 => "Tls1.3",
        _ when IsGrease(version) => "grease",
        _ => $"0x{version:X4}",
    };

    private static bool IsGrease(int value) => (value & 0x0F0F) == 0x0A0A && (value >> 8) == (value & 0xFF);

    private static string ExtensionName(int type) => type switch
    {
        0x0000 => "server_name",
        0x0001 => "max_fragment_length",
        0x0005 => "status_request",
        0x000A => "supported_groups",
        0x000B => "ec_point_formats",
        0x000D => "signature_algorithms",
        0x0010 => "application_layer_protocol_negotiation",
        0x0012 => "signed_certificate_timestamp",
        0x0015 => "padding",
        0x0016 => "encrypt_then_mac",
        0x0017 => "extended_master_secret",
        0x001B => "compress_certificate",
        0x0023 => "session_ticket",
        0x0029 => "pre_shared_key",
        0x002A => "early_data",
        0x002B => "supported_versions",
        0x002C => "cookie",
        0x002D => "psk_key_exchange_modes",
        0x0031 => "post_handshake_auth",
        0x0032 => "signature_algorithms_cert",
        0x0033 => "key_share",
        0x44CD => "application_settings",
        0xFE0D => "encrypted_client_hello",
        0xFF01 => "renegotiation_info",
        _ when IsGrease(type) => "grease",
        _ => $"0x{type:X4}",
    };

    private static string GroupName(int group) => group switch
    {
        0x0017 => "secp256r1",
        0x0018 => "secp384r1",
        0x0019 => "secp521r1",
        0x001D => "x25519",
        0x001E => "x448",
        0x11EC => "X25519MLKEM768",
        _ when IsGrease(group) => "grease",
        _ => $"0x{group:X4}",
    };

    private static string CipherSuiteName(int suite) => suite switch
    {
        0x1301 => "TLS_AES_128_GCM_SHA256",
        0x1302 => "TLS_AES_256_GCM_SHA384",
        0x1303 => "TLS_CHACHA20_POLY1305_SHA256",
        0xC02B => "TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256",
        0xC02C => "TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384",
        0xC02F => "TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256",
        0xC030 => "TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384",
        0xCCA8 => "TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256",
        0xCCA9 => "TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256",
        0xC013 => "TLS_ECDHE_RSA_WITH_AES_128_CBC_SHA",
        0xC014 => "TLS_ECDHE_RSA_WITH_AES_256_CBC_SHA",
        0x009C => "TLS_RSA_WITH_AES_128_GCM_SHA256",
        0x009D => "TLS_RSA_WITH_AES_256_GCM_SHA384",
        0x002F => "TLS_RSA_WITH_AES_128_CBC_SHA",
        0x0035 => "TLS_RSA_WITH_AES_256_CBC_SHA",
        _ when IsGrease(suite) => "grease",
        _ => "unknown",
    };

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes);

    /// <summary>A bounds-checked big-endian reader over a slice of a byte array.</summary>
    private sealed class Reader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _position;

        public Reader(byte[] data)
            : this(data, 0, data.Length)
        {
        }

        private Reader(byte[] data, int start, int end)
        {
            _data = data;
            _position = start;
            _end = end;
        }

        public bool AtEnd => _position >= _end;

        public int Remaining => _end - _position;

        public int U8() => Take(1)[0];

        public int U16()
        {
            var b = Take(2);
            return (b[0] << 8) | b[1];
        }

        public int U24()
        {
            var b = Take(3);
            return (b[0] << 16) | (b[1] << 8) | b[2];
        }

        public byte[] Bytes(int count) => Take(count).ToArray();

        public void Skip(int count) => Take(count);

        /// <summary>A reader over the next <paramref name="length"/> bytes, which this reader then skips.</summary>
        public Reader Sub(int length)
        {
            Take(length);
            return new Reader(_data, _position - length, _position);
        }

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > Remaining)
            {
                throw new InvalidOperationException("Read past the end of the TLS message.");
            }

            var span = new ReadOnlySpan<byte>(_data, _position, count);
            _position += count;
            return span;
        }
    }
}

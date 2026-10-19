using System;
using System.Collections.Generic;
using System.Text;

namespace Violens.ProxyHost.Client
{
    /// <summary>
    /// A request or response's start line, headers and body. Headers keep
    /// their order and case, and a name may appear more than once, as on the
    /// wire. Mutable, so a hook can change it in place the way FiddlerScript
    /// changes <c>oSession.oRequest</c>.
    /// </summary>
    public abstract class HttpMessage
    {
        /// <summary>The protocol version, such as <c>HTTP/1.1</c>.</summary>
        public string HttpVersion { get; set; } = "HTTP/1.1";

        /// <summary>The headers in order. Names are matched case-insensitively by the helpers below.</summary>
        public List<KeyValuePair<string, string>> Headers { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>The body exactly as it went over the wire: still compressed if it was sent compressed.</summary>
        public byte[] Body { get; set; } = new byte[0];

        /// <summary>The first header with this name, or null. Like Fiddler's <c>oRequest["Name"]</c>.</summary>
        public string? GetHeader(string name)
        {
            foreach (var header in Headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }

            return null;
        }

        /// <summary>Replaces every header with this name with one header, or adds it at the end.</summary>
        public void SetHeader(string name, string value)
        {
            var index = Headers.FindIndex(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase));
            RemoveHeader(name);
            var header = new KeyValuePair<string, string>(name, value);
            if (index >= 0 && index <= Headers.Count)
            {
                Headers.Insert(index, header);
            }
            else
            {
                Headers.Add(header);
            }
        }

        /// <summary>Removes every header with this name. Returns how many were removed.</summary>
        public int RemoveHeader(string name) =>
            Headers.RemoveAll(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Replaces the body with <paramref name="text"/> in UTF-8, and fixes
        /// up the headers to match: Content-Length set, and any
        /// Content-Encoding or Transfer-Encoding removed (the new body isn't
        /// compressed or chunked).
        /// </summary>
        public void SetBodyText(string text)
        {
            Body = Encoding.UTF8.GetBytes(text);
            RemoveHeader("Content-Encoding");
            RemoveHeader("Transfer-Encoding");
            SetHeader("Content-Length", Body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        internal void CopyFrom(HttpMessage other)
        {
            HttpVersion = other.HttpVersion;
            Headers = new List<KeyValuePair<string, string>>(other.Headers);
            Body = (byte[])other.Body.Clone();
        }
    }

    /// <summary>An HTTP request.</summary>
    public sealed class ProxyRequest : HttpMessage
    {
        /// <summary>GET, POST and so on.</summary>
        public string Method { get; set; } = "GET";

        /// <summary>
        /// The request target as sent: for HTTPS (inside a CONNECT tunnel) and
        /// for requests sent straight to a server, the path and query
        /// (<c>/search?q=1</c>); for plain HTTP through a proxy, the whole URL.
        /// </summary>
        public string Target { get; set; } = "/";

        /// <summary>A copy that can be changed without changing this one.</summary>
        public ProxyRequest Clone()
        {
            var copy = new ProxyRequest { Method = Method, Target = Target };
            copy.CopyFrom(this);
            return copy;
        }
    }

    /// <summary>An HTTP response.</summary>
    public sealed class ProxyResponse : HttpMessage
    {
        /// <summary>200, 404 and so on.</summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>The text after the status code, such as <c>OK</c>.</summary>
        public string ReasonPhrase { get; set; } = "OK";

        /// <summary>A copy that can be changed without changing this one.</summary>
        public ProxyResponse Clone()
        {
            var copy = new ProxyResponse { StatusCode = StatusCode, ReasonPhrase = ReasonPhrase };
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>A plain-text response, for answering a request without the server.</summary>
        public static ProxyResponse Text(int statusCode, string reasonPhrase, string text)
        {
            var response = new ProxyResponse { StatusCode = statusCode, ReasonPhrase = reasonPhrase };
            response.Headers.Add(new KeyValuePair<string, string>("Content-Type", "text/plain; charset=utf-8"));
            response.Headers.Add(new KeyValuePair<string, string>("Cache-Control", "no-store"));
            response.SetBodyText(text);
            return response;
        }
    }

    /// <summary>
    /// One finished request and its response, as the host captured it.
    /// </summary>
    public sealed class CapturedSession
    {
        /// <summary>The session's number, counting from 1, as it appears in a session list.</summary>
        public int Id { get; set; }

        /// <summary>The server: the CONNECT tunnel's host, or a plain HTTP request's.</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary><c>https</c> or <c>http</c>.</summary>
        public string Scheme { get; set; } = "https";

        /// <summary>When the request arrived.</summary>
        public DateTimeOffset StartedAt { get; set; }

        public ProxyRequest Request { get; set; } = new ProxyRequest();

        public ProxyResponse Response { get; set; } = new ProxyResponse();

        /// <summary>Fiddler-style string flags (<c>ui-backcolor</c>, <c>ui-comments</c>) set by hooks. Empty when there are none.</summary>
        public Dictionary<string, string> Flags { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The full URL, such as <c>https://example.com/search?q=1</c> (including any non-default port).</summary>
        public string Url { get; set; } = string.Empty;
    }
}

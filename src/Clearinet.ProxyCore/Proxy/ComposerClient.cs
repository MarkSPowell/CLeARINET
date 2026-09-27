using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Clearinet.ProxyCore.Http;

namespace Clearinet.ProxyCore.Proxy;

/// <summary>
/// Sends a request the way Fiddler Classic's Composer and "Reissue" did:
/// through CLeARINET's own listener, exactly as a browser would, so it's
/// captured as a new session and goes through everything live traffic does
/// (AutoResponder, breakpoints, FiddlerScript, extensions, the upstream
/// proxy).
///
/// HTTPS only, like the listener. The connection trusts CLeARINET's root
/// certificate specifically, so it works whether or not that root is in the
/// system's trust store.
/// </summary>
public static class ComposerClient
{
    /// <summary>
    /// Works out where a composed request goes. <paramref name="request"/> is
    /// as typed: its target may be an absolute https:// URL, or a path with
    /// a Host header. Returns the URL and the request to send (path-only
    /// target, Host header matching the URL), or false with an
    /// <paramref name="error"/> a person can act on.
    /// </summary>
    public static bool TryResolve(CapturedRequest request, out Uri? url, out CapturedRequest? toSend, out string? error)
    {
        url = null;
        toSend = null;
        error = null;

        var target = request.Target.Trim();
        if (target.StartsWith('/'))
        {
            var host = ContentDecoder.FindHeader(request.Headers, "Host");
            if (string.IsNullOrWhiteSpace(host))
            {
                error = "The request needs a full https:// URL, or a Host header to go with its path.";
                return false;
            }

            target = $"https://{host.Trim()}{target}";
        }

        if (!Uri.TryCreate(target, UriKind.Absolute, out var parsed))
        {
            error = $"\"{request.Target}\" isn't a URL. Start the first line with the method and an https:// URL, e.g. GET https://example.com/ HTTP/1.1";
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = $"CLeARINET only sends HTTPS requests so far, and this one is {parsed.Scheme}://.";
            return false;
        }

        var authority = parsed.IsDefaultPort ? parsed.Host : $"{parsed.Host}:{parsed.Port}";
        var headers = new List<(string Name, string Value)>(request.Headers.Count + 1);
        var hostSet = false;
        foreach (var (name, value) in request.Headers)
        {
            if (string.Equals(name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                if (!hostSet)
                {
                    headers.Add((name, authority));
                    hostSet = true;
                }

                continue;
            }

            headers.Add((name, value));
        }

        if (!hostSet)
        {
            headers.Insert(0, ("Host", authority));
        }

        var version = string.IsNullOrWhiteSpace(request.HttpVersion) || !request.HttpVersion.StartsWith("HTTP/1", StringComparison.OrdinalIgnoreCase)
            ? "HTTP/1.1"
            : request.HttpVersion;

        url = parsed;
        toSend = request with { Target = parsed.PathAndQuery, HttpVersion = version, Headers = headers };
        return true;
    }

    /// <summary>
    /// Sends <paramref name="request"/> (as returned by <see cref="TryResolve"/>)
    /// to <paramref name="url"/> through the listener on
    /// <paramref name="listenerPort"/>, and returns the response. The
    /// listener records the exchange as a session as it goes.
    /// </summary>
    public static async Task<CapturedResponse> SendAsync(
        int listenerPort,
        byte[] rootCertificateDer,
        Uri url,
        CapturedRequest request,
        CancellationToken cancellationToken)
    {
        using var root = X509CertificateLoader.LoadCertificate(rootCertificateDer);
        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, listenerPort, cancellationToken);
        var stream = client.GetStream();
        await UpstreamProxy.EstablishTunnelAsync(stream, url.Host, url.Port, "CLeARINET's own listener", cancellationToken);

        using var tls = new SslStream(stream, leaveInnerStreamOpen: false);
        await tls.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions
            {
                TargetHost = url.Host,
                EnabledSslProtocols = SslProtocols.None,
                RemoteCertificateValidationCallback = (_, certificate, _, _) => IsIssuedBy(certificate, root),
            },
            cancellationToken);

        await HttpMessageWriter.WriteRequestAsync(tls, request, cancellationToken);
        var isHead = string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
        return await Http1MessageReader.ReadResponseAsync(tls, isHead, cancellationToken)
            ?? throw new IOException("The connection closed before a response arrived.");
    }

    /// <summary>True when <paramref name="certificate"/> chains to <paramref name="root"/> (and only that root).</summary>
    private static bool IsIssuedBy(X509Certificate? certificate, X509Certificate2 root)
    {
        if (certificate is null)
        {
            return false;
        }

        using var leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(leaf);
    }
}

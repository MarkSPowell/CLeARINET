using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Violens.ProxyCore.Proxy.AutoConfig;

/// <summary>
/// Web Proxy Auto-Discovery (WPAD): finding the network's PAC script when
/// the system is set to "Automatically detect settings". On Windows this
/// asks WinHTTP, which tries DHCP and then DNS, as Windows itself does.
/// Elsewhere it tries DNS: <c>http://wpad.&lt;domain&gt;/wpad.dat</c> for this
/// machine's domain and each parent domain (stopping before the top two
/// labels, so it never asks a public domain such as <c>wpad.co.uk</c>).
/// </summary>
public static class WpadDiscovery
{
    /// <summary>The script's URL, or null when the network doesn't offer one.</summary>
    public static async Task<string?> FindScriptUrlAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            var found = await Task.Run(DetectWithWinHttp, cancellationToken);
            if (found is not null)
            {
                return found;
            }
        }

        foreach (var host in CandidateHosts(DomainName()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                if (addresses.Length > 0)
                {
                    return $"http://{host}/wpad.dat";
                }
            }
            catch (SocketException)
            {
                // Not there; try the parent domain.
            }
        }

        return null;
    }

    /// <summary>
    /// "wpad.a.b.example.com", "wpad.b.example.com", "wpad.example.com" for
    /// the domain "a.b.example.com". Internal for tests.
    /// </summary>
    internal static IReadOnlyList<string> CandidateHosts(string? domain)
    {
        var labels = (domain ?? string.Empty).Trim().TrimEnd('.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries);
        var hosts = new List<string>();
        for (var start = 0; labels.Length - start >= 2; start++)
        {
            hosts.Add("wpad." + string.Join('.', labels[start..]));
        }

        return hosts;
    }

    private static string? DomainName()
    {
        try
        {
            var domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            return string.IsNullOrWhiteSpace(domain) ? null : domain;
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? DetectWithWinHttp()
    {
        const int DetectWithDhcp = 0x1;
        const int DetectWithDnsA = 0x2;

        try
        {
            if (!WinHttpDetectAutoProxyConfigUrl(DetectWithDhcp | DetectWithDnsA, out var url) || url == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(url);
            }
            finally
            {
                GlobalFree(url);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("winhttp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WinHttpDetectAutoProxyConfigUrl(int autoDetectFlags, out IntPtr autoConfigUrl);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}

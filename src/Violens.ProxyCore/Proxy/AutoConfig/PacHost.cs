using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Violens.ProxyCore.Proxy.AutoConfig;

/// <summary>
/// What a PAC script can ask about the network: name lookups
/// (<c>dnsResolve</c>, <c>isResolvable</c>, <c>isInNet</c>) and this
/// machine's own address (<c>myIpAddress</c>). An interface so tests can
/// answer without a real network.
/// </summary>
public interface IPacHost
{
    /// <summary>The addresses <paramref name="host"/> resolves to; empty when it doesn't resolve.</summary>
    IReadOnlyList<IPAddress> Resolve(string host);

    /// <summary>This machine's addresses, the one used to reach the internet first.</summary>
    IReadOnlyList<IPAddress> LocalAddresses();

    /// <summary>Where a script's <c>alert()</c> output goes.</summary>
    void Log(string message);
}

/// <summary>The real network, for scripts VioLens actually runs.</summary>
public sealed class SystemPacHost : IPacHost
{
    // A PAC script runs while a request waits, so a slow lookup mustn't
    // hold it up for long. Browsers time these out too.
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(2);

    public static SystemPacHost Instance { get; } = new();

    public IReadOnlyList<IPAddress> Resolve(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return [];
        }

        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            return [literal];
        }

        try
        {
            var lookup = Dns.GetHostAddressesAsync(host);
            return lookup.Wait(LookupTimeout) ? lookup.Result : [];
        }
        catch (Exception ex) when (ex is SocketException or AggregateException or ArgumentException)
        {
            return [];
        }
    }

    public IReadOnlyList<IPAddress> LocalAddresses()
    {
        var addresses = new List<IPAddress>();

        // The address this machine would use to reach the internet: a UDP
        // "connect" picks the route without sending anything.
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(IPAddress.Parse("192.0.2.1"), 9); // TEST-NET-1: never actually contacted
            if (socket.LocalEndPoint is IPEndPoint { Address: var primary } && !IPAddress.Any.Equals(primary))
            {
                addresses.Add(primary);
            }
        }
        catch (SocketException)
        {
            // No route at all; fall through to the interface list.
        }

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (!IPAddress.IsLoopback(address) && !address.IsIPv6LinkLocal && !addresses.Contains(address))
                    {
                        addresses.Add(address);
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        return addresses;
    }

    public void Log(string message) => ProxyEngine.Log("pac", message);
}

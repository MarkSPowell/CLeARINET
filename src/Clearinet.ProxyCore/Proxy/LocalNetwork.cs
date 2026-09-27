using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Clearinet.ProxyCore.Proxy;

/// <summary>This machine's own network addresses, for telling another device where to find CLeARINET.</summary>
public static class LocalNetwork
{
    /// <summary>
    /// The IPv4 addresses of network interfaces that are up, excluding
    /// loopback and link-local (169.254.x.x) addresses: the ones a phone on
    /// the same network can reach. Never throws; empty if they can't be read.
    /// </summary>
    public static IReadOnlyList<IPAddress> GetIPv4Addresses()
    {
        var addresses = new List<IPAddress>();
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
                    if (address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address) &&
                        !IsLinkLocal(address) &&
                        !addresses.Contains(address))
                    {
                        addresses.Add(address);
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }

        return addresses;
    }

    /// <summary>True when <paramref name="host"/> names this machine: localhost, a loopback address, or one of its own addresses.</summary>
    public static bool IsThisMachine(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            return false;
        }

        return IPAddress.IsLoopback(address) || GetIPv4Addresses().Contains(address);
    }

    private static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }
}

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ModularAudience.CoOp;

public static class CoOpNetworkUtilities
{
    public static IReadOnlyList<IPAddress> GetLocalIPv4Addresses()
    {
        var addresses = new List<IPAddress>();
        try
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(unicast.Address))
                    {
                        addresses.Add(unicast.Address);
                    }
                }
            }
        }
        catch
        {
            // Network information is best effort. The loopback fallback below keeps hosting usable.
        }

        return addresses
            .Distinct()
            .OrderByDescending(IsPrivateAddress)
            .ToList();
    }

    public static string GetPreferredLocalAddress()
    {
        return GetLocalIPv4Addresses().FirstOrDefault()?.ToString() ?? IPAddress.Loopback.ToString();
    }

    public static IReadOnlyList<IPAddress> GetDiscoveryBroadcastAddresses()
    {
        var addresses = new List<IPAddress> { IPAddress.Broadcast, IPAddress.Loopback };
        try
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (UnicastIPAddressInformation unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                        unicast.IPv4Mask is null)
                    {
                        continue;
                    }

                    IPAddress? broadcast = GetBroadcastAddress(unicast.Address, unicast.IPv4Mask);
                    if (broadcast != null && !addresses.Contains(broadcast))
                    {
                        addresses.Add(broadcast);
                    }
                }
            }
        }
        catch
        {
            // Keep the global broadcast and loopback entries.
        }

        return addresses;
    }

    private static IPAddress? GetBroadcastAddress(IPAddress address, IPAddress mask)
    {
        byte[] addressBytes = address.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        if (addressBytes.Length != 4 || maskBytes.Length != 4)
        {
            return null;
        }

        var broadcastBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            broadcastBytes[i] = (byte)(addressBytes[i] | (byte)~maskBytes[i]);
        }

        return new IPAddress(broadcastBytes);
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        return bytes[0] == 10 ||
            bytes[0] == 192 && bytes[1] == 168 ||
            bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }
}

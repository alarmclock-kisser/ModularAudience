using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ModularAudience.CoOp;

internal sealed record CoOpLocalIPv4Network(
    IPAddress Address,
    IPAddress SubnetMask,
    bool HasGateway,
    bool IsLanAdapter,
    bool IsTunnel);

public static class CoOpNetworkUtilities
{
    public static IReadOnlyList<IPAddress> GetLocalIPv4Addresses()
    {
        return OrderLocalNetworks(GetLocalIPv4Networks())
            .Select(network => network.Address)
            .Distinct()
            .ToList();
    }

    internal static IReadOnlyList<CoOpLocalIPv4Network> GetLocalIPv4Networks()
    {
        var networks = new List<CoOpLocalIPv4Network>();
        try
        {
            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                IPInterfaceProperties properties = networkInterface.GetIPProperties();
                bool hasGateway = properties.GatewayAddresses.Any(gateway =>
                    gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any));
                NetworkInterfaceType type = networkInterface.NetworkInterfaceType;
                foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(unicast.Address) && unicast.IPv4Mask is not null)
                    {
                        networks.Add(new CoOpLocalIPv4Network(unicast.Address, unicast.IPv4Mask, hasGateway,
                            type is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 or
                                NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx,
                            type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp));
                    }
                }
            }
        }
        catch
        {
            // Network information is best effort. The loopback fallback below keeps hosting usable.
        }

        return networks;
    }

    internal static IReadOnlyList<CoOpLocalIPv4Network> OrderLocalNetworks(IEnumerable<CoOpLocalIPv4Network> networks)
    {
        return networks
            .OrderBy(network => network.IsTunnel)
            .ThenByDescending(network => network.IsLanAdapter)
            .ThenByDescending(network => network.HasGateway)
            .ThenByDescending(network => IsPrivateAddress(network.Address))
            .ToList();
    }

    public static string GetPreferredLocalAddress()
    {
        return GetLocalIPv4Addresses().FirstOrDefault()?.ToString() ?? IPAddress.Loopback.ToString();
    }

    public static IReadOnlyList<IPAddress> GetDiscoveryBroadcastAddresses()
    {
        return GetDiscoveryBroadcastAddresses(GetLocalIPv4Networks());
    }

    internal static IReadOnlyList<IPAddress> GetDiscoveryBroadcastAddresses(IEnumerable<CoOpLocalIPv4Network> networks)
    {
        var addresses = new List<IPAddress> { IPAddress.Loopback };
        foreach (CoOpLocalIPv4Network network in OrderLocalNetworks(networks))
        {
            if (network.IsTunnel)
            {
                continue;
            }

            IPAddress? broadcast = GetBroadcastAddress(network.Address, network.SubnetMask);
            if (broadcast != null && !addresses.Contains(broadcast)) addresses.Add(broadcast);
        }

        // Prefer explicit local subnets over a limited broadcast sent through
        // whichever adapter (possibly a VPN) owns the default route.
        if (addresses.Count == 1) addresses.Add(IPAddress.Broadcast);
        return addresses;
    }

    internal static IReadOnlyList<IPAddress> SelectConnectionAddresses(
        IEnumerable<IPAddress> resolvedAddresses,
        IReadOnlyList<CoOpLocalIPv4Network> localNetworks)
    {
        List<IPAddress> addresses = resolvedAddresses.Distinct().ToList();
        List<IPAddress> local = addresses.Where(address => IPAddress.IsLoopback(address) ||
            localNetworks.Any(network => !network.IsTunnel && IsInSameSubnet(address, network))).ToList();

        // If a host resolves to both LAN and Internet addresses, stay on the
        // LAN candidates. A failed LAN connection must not silently use WAN.
        return local.Count > 0 ? local : addresses;
    }

    internal static bool IsInSameSubnet(IPAddress address, CoOpLocalIPv4Network network)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork || !IsValidIPv4Mask(network.SubnetMask)) return false;
        byte[] addressBytes = address.GetAddressBytes();
        byte[] localBytes = network.Address.GetAddressBytes();
        byte[] maskBytes = network.SubnetMask.GetAddressBytes();
        if (localBytes.Length != 4) return false;
        for (int index = 0; index < 4; index++)
        {
            if ((addressBytes[index] & maskBytes[index]) != (localBytes[index] & maskBytes[index])) return false;
        }
        return true;
    }

    private static IPAddress? GetBroadcastAddress(IPAddress address, IPAddress mask)
    {
        byte[] addressBytes = address.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        if (addressBytes.Length != 4 || !IsValidIPv4Mask(mask))
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

    private static bool IsValidIPv4Mask(IPAddress mask)
    {
        byte[] bytes = mask.GetAddressBytes();
        if (bytes.Length != 4 || bytes.All(value => value == 0)) return false;
        bool zeroSeen = false;
        foreach (byte value in bytes)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                bool set = (value & (1 << bit)) != 0;
                if (set && zeroSeen) return false;
                if (!set) zeroSeen = true;
            }
        }
        return true;
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

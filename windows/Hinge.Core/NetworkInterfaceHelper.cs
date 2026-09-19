using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Hinge.Core;

public sealed record PhysicalLanEndpoint(
    NetworkInterface Interface,
    IPAddress Address,
    IPAddress Mask,
    IPAddress Broadcast);

public static class NetworkInterfaceHelper
{
    private static readonly string[] VirtualOrVpnKeywords =
    {
        "vpn", "tap", "tun", "wintun", "wireguard", "openvpn",
        "cisco", "anyconnect", "fortinet", "forticlient", "globalprotect",
        "sangfor", "easyconnect", "tailscale", "zerotier", "softether",
        "clash", "sing-box", "shadowsocks", "v2ray", "meta tunnel",
        "nexgen", "hyper-v", "vmware", "virtualbox", "virtual",
        "pseudo", "npcap", "teredo", "6to4", "bluetooth", "wan miniport",
        "qemu", "veth", "docker", "wsl"
    };

    /// <summary>
    /// Checks whether the given network interface is a virtual adapter, VPN tunnel,
    /// or host-only virtual switch rather than a genuine physical LAN/Wi-Fi adapter.
    /// </summary>
    public static bool IsVirtualOrVpnInterface(NetworkInterface nic)
    {
        if (nic == null) return true;
        if (nic.OperationalStatus != OperationalStatus.Up) return true;

        if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or
            NetworkInterfaceType.Tunnel or
            NetworkInterfaceType.Ppp or
            NetworkInterfaceType.Slip)
        {
            return true;
        }

        string name = nic.Name ?? string.Empty;
        string desc = nic.Description ?? string.Empty;

        foreach (var keyword in VirtualOrVpnKeywords)
        {
            if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                desc.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether an IPv4 address belongs to a virtual, loopback, APIPA, or RFC 2544 benchmark range.
    /// </summary>
    public static bool IsReservedOrVirtualIp(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return true;
        byte[] bytes = address.GetAddressBytes();

        // 127.0.0.0/8 (Loopback)
        if (bytes[0] == 127) return true;

        // 0.0.0.0/8 (Current network)
        if (bytes[0] == 0) return true;

        // 169.254.0.0/16 (Link-local / APIPA)
        if (bytes[0] == 169 && bytes[1] == 254) return true;

        // 198.18.0.0/15 (RFC 2544 benchmark, standard fake-ip range used by Clash / sing-box / tun2socks)
        if (bytes[0] == 198 && (bytes[1] == 18 || bytes[1] == 19)) return true;

        return false;
    }

    /// <summary>
    /// Calculates the directed broadcast address for a given IPv4 address and subnet mask.
    /// </summary>
    public static IPAddress CalculateBroadcastAddress(IPAddress address, IPAddress mask)
    {
        byte[] ipBytes = address.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        var broadcast = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            broadcast[i] = (byte)(ipBytes[i] | (byte)~maskBytes[i]);
        }
        return new IPAddress(broadcast);
    }

    /// <summary>
    /// Discovers all active physical LAN and Wi-Fi endpoints on this machine.
    /// </summary>
    public static IReadOnlyList<PhysicalLanEndpoint> GetPhysicalLanEndpoints()
    {
        var endpoints = new List<PhysicalLanEndpoint>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (IsVirtualOrVpnInterface(nic)) continue;

            IPInterfaceProperties properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IsReservedOrVirtualIp(unicast.Address)) continue;

                var mask = unicast.IPv4Mask ?? IPAddress.Parse("255.255.255.0");
                var broadcast = CalculateBroadcastAddress(unicast.Address, mask);

                endpoints.Add(new PhysicalLanEndpoint(nic, unicast.Address, mask, broadcast));
            }
        }

        return endpoints;
    }

    /// <summary>
    /// Returns a list of all local physical IPv4 addresses as strings.
    /// </summary>
    public static List<string> GetPhysicalCandidateAddresses()
    {
        return GetPhysicalLanEndpoints()
            .Select(ep => ep.Address.ToString())
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Finds a local physical endpoint that shares the same subnet with the target remote IP.
    /// </summary>
    public static PhysicalLanEndpoint? FindMatchingPhysicalLanEndpoint(IPAddress targetIp)
    {
        if (targetIp.AddressFamily != AddressFamily.InterNetwork) return null;
        byte[] targetBytes = targetIp.GetAddressBytes();

        var endpoints = GetPhysicalLanEndpoints();
        foreach (var ep in endpoints)
        {
            byte[] localBytes = ep.Address.GetAddressBytes();
            byte[] maskBytes = ep.Mask.GetAddressBytes();

            bool sameSubnet = true;
            for (int i = 0; i < 4; i++)
            {
                if ((targetBytes[i] & maskBytes[i]) != (localBytes[i] & maskBytes[i]))
                {
                    sameSubnet = false;
                    break;
                }
            }

            if (sameSubnet)
            {
                return ep;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a local physical IP that shares the same subnet with the target remote IP.
    /// Used to explicitly bind sockets before connecting or sending probes.
    /// </summary>
    public static IPAddress? FindMatchingLocalPhysicalAddress(IPAddress targetIp)
    {
        var matchingEp = FindMatchingPhysicalLanEndpoint(targetIp);
        if (matchingEp != null) return matchingEp.Address;

        if (!IPAddress.IsLoopback(targetIp))
        {
            var endpoints = GetPhysicalLanEndpoints();
            if (endpoints.Count > 0) return endpoints[0].Address;
        }

        return null;
    }
}

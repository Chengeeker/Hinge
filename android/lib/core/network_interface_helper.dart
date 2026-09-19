import 'dart:io';

class PhysicalInterfaceEndpoint {
  final NetworkInterface networkInterface;
  final InternetAddress address;
  final InternetAddress broadcast;
  final int prefixLength;

  const PhysicalInterfaceEndpoint({
    required this.networkInterface,
    required this.address,
    required this.broadcast,
    required this.prefixLength,
  });
}

class NetworkInterfaceHelper {
  static const List<String> _virtualKeywords = [
    'tun',
    'tap',
    'ppp',
    'p2p',
    'dummy',
    'lo',
    'rmnet',
    'sit',
    'ip6tnl',
    'wsl',
    'docker',
    'veth',
    'vpn',
    'wireguard',
    'wintun',
    'clash',
    'singbox',
    'shadowsocks',
    'tailscale',
    'zerotier',
    'meta',
    'nexgen',
  ];

  /// Checks whether an interface name suggests it is a virtual or VPN tunnel.
  static bool isVirtualOrVpnInterfaceName(String name) {
    final lower = name.toLowerCase();
    for (final keyword in _virtualKeywords) {
      if (lower.contains(keyword)) {
        return true;
      }
    }
    return false;
  }

  /// Checks whether an IPv4 address is in loopback, APIPA, or RFC 2544 benchmark ranges.
  static bool isReservedOrVirtualIp(InternetAddress address) {
    if (address.type != InternetAddressType.IPv4) return true;
    final parts = address.address.split('.');
    if (parts.length != 4) return true;
    final b0 = int.tryParse(parts[0]) ?? 0;
    final b1 = int.tryParse(parts[1]) ?? 0;

    // Loopback (127.0.0.0/8)
    if (b0 == 127) return true;
    // Current network (0.0.0.0/8)
    if (b0 == 0) return true;
    // APIPA / Link-local (169.254.0.0/16)
    if (b0 == 169 && b1 == 254) return true;
    // RFC 2544 benchmark (198.18.0.0/15, used by Clash/sing-box/tun2socks fake-IP)
    if (b0 == 198 && (b1 == 18 || b1 == 19)) return true;

    return false;
  }

  /// Safely calculates the directed broadcast address for an IPv4 address and prefix length.
  static InternetAddress calculateBroadcastAddress(
    InternetAddress address, [
    int prefixLength = 24,
  ]) {
    final parts = address.address.split('.');
    if (parts.length != 4) return InternetAddress('255.255.255.255');
    final octets = parts.map((p) => int.tryParse(p) ?? 0).toList();
    final prefix = prefixLength.clamp(8, 30);

    final ip =
        (octets[0] << 24) | (octets[1] << 16) | (octets[2] << 8) | octets[3];
    final mask = (0xffffffff << (32 - prefix)) & 0xffffffff;
    final broadcastInt = (ip | (~mask & 0xffffffff)) & 0xffffffff;

    final b0 = (broadcastInt >> 24) & 0xff;
    final b1 = (broadcastInt >> 16) & 0xff;
    final b2 = (broadcastInt >> 8) & 0xff;
    final b3 = broadcastInt & 0xff;

    return InternetAddress('$b0.$b1.$b2.$b3');
  }

  /// Discovers all physical LAN/Wi-Fi endpoints on this device.
  static Future<List<PhysicalInterfaceEndpoint>>
  getPhysicalLanEndpoints() async {
    final endpoints = <PhysicalInterfaceEndpoint>[];
    try {
      final interfaces = await NetworkInterface.list(
        type: InternetAddressType.IPv4,
        includeLoopback: false,
        includeLinkLocal: false,
      );

      for (final nic in interfaces) {
        if (isVirtualOrVpnInterfaceName(nic.name)) continue;

        for (final addr in nic.addresses) {
          if (isReservedOrVirtualIp(addr)) continue;

          var prefix = 24;
          InternetAddress? directedBroadcast;

          try {
            final dynamic dynamicAddr = addr;
            final dynamic p = dynamicAddr.prefixLength;
            if (p is int && p > 0 && p <= 32) {
              prefix = p;
            }
            final dynamic b = dynamicAddr.broadcast;
            if (b is InternetAddress) {
              directedBroadcast = b;
            }
          } catch (_) {
            // Getter might not be present on all Dart platforms
          }

          directedBroadcast ??= calculateBroadcastAddress(addr, prefix);

          endpoints.add(
            PhysicalInterfaceEndpoint(
              networkInterface: nic,
              address: addr,
              broadcast: directedBroadcast,
              prefixLength: prefix,
            ),
          );
        }
      }
    } catch (_) {
      // Network listing failure fallback
    }

    return endpoints;
  }

  /// Returns candidate physical IPv4 address strings.
  static Future<List<String>> getPhysicalCandidateAddresses() async {
    final endpoints = await getPhysicalLanEndpoints();
    return endpoints.map((e) => e.address.address).toSet().toList();
  }

  /// Finds a local physical IP that shares the same subnet with the target IP.
  static Future<InternetAddress?> findMatchingLocalPhysicalAddress(
    InternetAddress targetIp,
  ) async {
    if (targetIp.type != InternetAddressType.IPv4) return null;
    final targetParts = targetIp.address.split('.');
    if (targetParts.length != 4) return null;
    final t0 = int.tryParse(targetParts[0]) ?? 0;
    final t1 = int.tryParse(targetParts[1]) ?? 0;
    final t2 = int.tryParse(targetParts[2]) ?? 0;
    final t3 = int.tryParse(targetParts[3]) ?? 0;
    final targetInt = (t0 << 24) | (t1 << 16) | (t2 << 8) | t3;

    final endpoints = await getPhysicalLanEndpoints();
    for (final ep in endpoints) {
      final localParts = ep.address.address.split('.');
      if (localParts.length != 4) continue;
      final l0 = int.tryParse(localParts[0]) ?? 0;
      final l1 = int.tryParse(localParts[1]) ?? 0;
      final l2 = int.tryParse(localParts[2]) ?? 0;
      final l3 = int.tryParse(localParts[3]) ?? 0;
      final localInt = (l0 << 24) | (l1 << 16) | (l2 << 8) | l3;

      final prefix = ep.prefixLength.clamp(8, 30);
      final mask = (0xffffffff << (32 - prefix)) & 0xffffffff;

      if ((targetInt & mask) == (localInt & mask)) {
        return ep.address;
      }
    }

    if (endpoints.isNotEmpty && !targetIp.isLoopback) {
      return endpoints.first.address;
    }

    return null;
  }
}

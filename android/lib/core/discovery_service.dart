import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart';

import 'constants.dart';
import 'device_identity_manager.dart';
import 'device_registry.dart';
import 'discovery_message.dart';
import 'network_interface_helper.dart';

class DiscoveryConnectionRequest {
  final DiscoveryMessage message;
  final String remoteAddress;

  const DiscoveryConnectionRequest({
    required this.message,
    required this.remoteAddress,
  });
}

class DiscoveryService {
  static const MethodChannel _platform = MethodChannel('hinge/platform');
  final DeviceIdentity _localIdentity;
  final DeviceRegistry _registry;
  final int _listenPort;
  final int Function()? sessionPortProvider;

  RawDatagramSocket? _socket;
  Timer? _broadcastTimer;
  Timer? _subnetProbeTimer;
  Timer? _pruneTimer;
  bool _isRunning = false;
  bool _isListening = false;
  String? _lastError;
  bool pairingRequired = false;
  List<String> _cachedPhysicalAddresses = const [];
  final Map<String, DateTime> _lastPeerReplies = {};
  final Map<String, DateTime> _lastConnectionRequests = {};
  final StreamController<DiscoveryConnectionRequest>
  _connectionRequestController =
      StreamController<DiscoveryConnectionRequest>.broadcast();

  bool get isRunning => _isRunning;
  bool get isListening => _isListening;
  String? get lastError => _lastError;
  DeviceRegistry get registry => _registry;
  Stream<DiscoveryConnectionRequest> get connectionRequests =>
      _connectionRequestController.stream;

  DiscoveryService({
    required this._localIdentity,
    required this._registry,
    this._listenPort = AppConstants.discoveryUdpPort,
    this.sessionPortProvider,
  });

  /// Android can keep cellular data and Wi-Fi active at the same time. Bind
  /// the process before Dart creates its LAN sockets so discovery uses Wi-Fi.
  Future<bool> prepareNetwork() async {
    if (!Platform.isAndroid) return false;
    try {
      final result = await _platform.invokeMethod<dynamic>(
        'bindDiscoveryToWifi',
      );
      if (result is Map) return result['bound'] == true;
      return result == true;
    } catch (_) {
      // The Dart socket still has its normal route as a fallback.
      return false;
    }
  }

  Future<void> _bindSocket(int port) async {
    _socket = await RawDatagramSocket.bind(
      InternetAddress.anyIPv4,
      port,
      reuseAddress: true,
    );
    _socket?.broadcastEnabled = true;
    _socket?.listen(_onSocketEvent, onError: _onSocketError);
  }

  Future<void> start() async {
    if (_isRunning) return;

    await prepareNetwork();
    if (Platform.isAndroid) {
      try {
        await _platform.invokeMethod<bool>('acquireDiscoveryMulticastLock');
      } catch (_) {
        // Discovery still has unicast fallbacks on devices without this API.
      }
    }

    try {
      await _bindSocket(_listenPort);
      _isListening = true;
      _lastError = null;
    } catch (error) {
      _lastError = '无法监听 UDP $_listenPort：$error';
      try {
        // A temporary port remains usable when it is advertised through the
        // discoveryPort field. Older peers continue to use 52830.
        await _bindSocket(0);
        _isListening = true;
      } catch (fallbackError) {
        _socket = null;
        _isListening = false;
        _lastError = '设备发现服务启动失败：$fallbackError';
      }
    }

    _isRunning = true;

    _broadcastTimer = Timer.periodic(
      const Duration(seconds: 3),
      (_) => broadcastOnce(),
    );
    broadcastOnce();
    // Some access points drop broadcast packets between Wi-Fi clients. A
    // small, rate-limited unicast sweep covers the common /24 subnet case.
    _subnetProbeTimer = Timer.periodic(
      const Duration(seconds: 10),
      (_) => probeLocalSubnets(),
    );
    Future<void>.delayed(const Duration(seconds: 1), () {
      if (_isRunning) probeLocalSubnets();
    });

    _pruneTimer = Timer.periodic(
      const Duration(seconds: 2),
      (_) => _registry.pruneOffline(const Duration(seconds: 30)),
    );
  }

  void _onSocketError(Object error, StackTrace stackTrace) {
    if (_isRunning) _lastError = '设备发现 UDP socket 错误：$error';
  }

  /// Rebinds only the UDP discovery socket after Android returns from the
  /// background or moves to another Wi-Fi network. Active TCP sessions stay
  /// untouched.
  Future<void> refreshNetwork() async {
    await prepareNetwork();
    if (!_isRunning) return;

    final previous = _socket;
    _socket = null;
    previous?.close();
    try {
      await _bindSocket(_listenPort);
      _isListening = true;
      _lastError = null;
    } catch (error) {
      _lastError = '无法重新绑定 UDP $_listenPort：$error';
      try {
        await _bindSocket(0);
        _isListening = true;
      } catch (fallbackError) {
        _socket = null;
        _isListening = false;
        _lastError = '设备发现服务重新启动失败：$fallbackError';
        return;
      }
    }
    await broadcastOnce();
  }

  void _onSocketEvent(RawSocketEvent event) {
    if (event == RawSocketEvent.read && _socket != null) {
      final datagram = _socket!.receive();
      if (datagram == null) return;

      try {
        final text = utf8.decode(datagram.data);
        final json = jsonDecode(text) as Map<String, dynamic>;
        final message = DiscoveryMessage.fromJson(json);

        if (message.deviceId != _localIdentity.deviceId) {
          _registry.upsertDevice(message, datagram.address.address);
          final now = DateTime.now();
          if (message.connectionRequested) {
            final lastRequest = _lastConnectionRequests[message.deviceId];
            if (lastRequest == null ||
                now.difference(lastRequest) >= const Duration(seconds: 2)) {
              _lastConnectionRequests[message.deviceId] = now;
              _connectionRequestController.add(
                DiscoveryConnectionRequest(
                  message: message,
                  remoteAddress: datagram.address.address,
                ),
              );
            }
          }
          final lastReply = _lastPeerReplies[message.deviceId];
          if (lastReply == null || now.difference(lastReply).inSeconds >= 5) {
            _lastPeerReplies[message.deviceId] = now;
            probeManualIp(datagram.address.address, message.discoveryPort);
          }
        }
      } catch (_) {
        // Ignore malformed UDP packets
      }
    }
  }

  Future<void> broadcastOnce() async {
    bool sent = false;
    String? lastSendError;

    // Refresh candidate physical addresses
    final physicalEndpoints =
        await NetworkInterfaceHelper.getPhysicalLanEndpoints();
    _cachedPhysicalAddresses =
        physicalEndpoints.map((e) => e.address.address).toSet().toList();

    final message = _createDiscoveryMessage();
    final data = utf8.encode(jsonEncode(message.toJson()));

    // 1. Explicitly bind and broadcast out of EVERY physical LAN/Wi-Fi interface.
    // This forces packets out of wlan0/eth0 even if a VPN tunnel default route is active.
    for (final ep in physicalEndpoints) {
      RawDatagramSocket? boundSocket;
      try {
        boundSocket = await RawDatagramSocket.bind(ep.address, 0);
        boundSocket.broadcastEnabled = true;
        boundSocket.send(data, InternetAddress('255.255.255.255'), _listenPort);
        boundSocket.send(data, ep.broadcast, _listenPort);
        sent = true;
      } catch (error) {
        lastSendError = '$error';
      } finally {
        boundSocket?.close();
      }
    }

    // 2. Fallback: also send through the main socket if available
    final socket = _socket;
    if (socket != null) {
      try {
        final targets = <InternetAddress>[InternetAddress('255.255.255.255')];
        for (final ep in physicalEndpoints) {
          if (!targets.any((t) => t.address == ep.broadcast.address)) {
            targets.add(ep.broadcast);
          }
        }
        for (final target in targets) {
          try {
            final bytesSent = socket.send(data, target, _listenPort);
            sent = sent || bytesSent > 0;
          } catch (error) {
            lastSendError ??= '$error';
          }
        }
      } catch (error) {
        lastSendError ??= '$error';
      }
    }

    if (sent) {
      // A fallback sender may be active when the standard port is occupied;
      // keep that diagnostic visible even if outbound broadcast succeeds.
      if (_isListening) _lastError = null;
    } else if (lastSendError != null) {
      _lastError = '无法发送局域网发现广播：$lastSendError';
    }
  }

  Future<void> probeLocalSubnets() async {
    final physicalEndpoints =
        await NetworkInterfaceHelper.getPhysicalLanEndpoints();
    final message = _createDiscoveryMessage();
    final data = utf8.encode(jsonEncode(message.toJson()));
    var sent = false;

    for (final ep in physicalEndpoints) {
      final octets = _ipv4Octets(ep.address.address);
      if (octets == null) continue;

      final prefix = ep.prefixLength < 24 ? 24 : ep.prefixLength;
      if (prefix > 30) continue;
      final base = _networkBase(octets, prefix);
      final hostCount = 1 << (32 - prefix);

      RawDatagramSocket? boundSocket;
      try {
        boundSocket = await RawDatagramSocket.bind(ep.address, 0);
        for (var host = 1; host < hostCount - 1; host++) {
          final target = '${base[0]}.${base[1]}.${base[2]}.$host';
          if (target == ep.address.address) continue;
          try {
            sent =
                boundSocket.send(data, InternetAddress(target), _listenPort) >
                    0 ||
                sent;
          } catch (_) {
            // A host may be offline or filtered; continue probing others.
          }
        }
      } catch (_) {
      } finally {
        boundSocket?.close();
      }
    }

    // Also fallback to probing from main socket if needed
    final socket = _socket;
    if (socket != null && !sent) {
      for (final ep in physicalEndpoints) {
        final octets = _ipv4Octets(ep.address.address);
        if (octets == null) continue;
        final prefix = ep.prefixLength < 24 ? 24 : ep.prefixLength;
        if (prefix > 30) continue;
        final base = _networkBase(octets, prefix);
        final hostCount = 1 << (32 - prefix);
        for (var host = 1; host < hostCount - 1; host++) {
          final target = '${base[0]}.${base[1]}.${base[2]}.$host';
          if (target == ep.address.address) continue;
          try {
            sent =
                socket.send(data, InternetAddress(target), _listenPort) > 0 ||
                sent;
          } catch (_) {}
        }
      }
    }

    if (sent && _isListening && _lastError?.startsWith('无法发送') == true) {
      _lastError = null;
    }
  }

  List<int>? _ipv4Octets(String value) {
    final parts = value.split('.');
    if (parts.length != 4) return null;
    final octets = <int>[];
    for (final part in parts) {
      final value = int.tryParse(part);
      if (value == null || value < 0 || value > 255) return null;
      octets.add(value);
    }
    return octets;
  }

  List<int> _networkBase(List<int> octets, int prefixLength) {
    final ip =
        (octets[0] << 24) | (octets[1] << 16) | (octets[2] << 8) | octets[3];
    final mask = (0xffffffff << (32 - prefixLength)) & 0xffffffff;
    final network = ip & mask;
    return [
      (network >> 24) & 0xff,
      (network >> 16) & 0xff,
      (network >> 8) & 0xff,
      network & 0xff,
    ];
  }

  void probeManualIp(
    String ip, [
    int port = AppConstants.discoveryUdpPort,
  ]) async {
    final message = _createDiscoveryMessage();
    final data = utf8.encode(jsonEncode(message.toJson()));
    final parsedPort = parseNetworkPort(port, AppConstants.discoveryUdpPort);

    try {
      final targetAddr = InternetAddress(ip);
      final matchingIp =
          await NetworkInterfaceHelper.findMatchingLocalPhysicalAddress(
            targetAddr,
          );
      if (matchingIp != null) {
        RawDatagramSocket? boundSocket;
        try {
          boundSocket = await RawDatagramSocket.bind(matchingIp, 0);
          boundSocket.send(data, targetAddr, parsedPort);
        } catch (_) {
        } finally {
          boundSocket?.close();
        }
      }
    } catch (_) {}

    _socket?.send(data, InternetAddress(ip), parsedPort);
  }

  /// Asks the peer to open the TCP session in the opposite direction.
  /// UDP discovery is often reachable even when a renamed Windows executable
  /// has not yet been granted an inbound TCP firewall exception.
  void requestReverseConnection(
    String ip, [
    int port = AppConstants.discoveryUdpPort,
    bool automaticReconnect = false,
  ]) async {
    final message = _createDiscoveryMessage(
      connectionRequested: true,
      automaticReconnect: automaticReconnect,
      pairingRequired: pairingRequired,
    );
    final data = utf8.encode(jsonEncode(message.toJson()));
    final parsedPort = parseNetworkPort(port, AppConstants.discoveryUdpPort);

    try {
      final targetAddr = InternetAddress(ip);
      final matchingIp =
          await NetworkInterfaceHelper.findMatchingLocalPhysicalAddress(
            targetAddr,
          );
      for (int burst = 0; burst < 3; burst++) {
        if (burst > 0) {
          await Future<void>.delayed(const Duration(milliseconds: 120));
        }
        if (matchingIp != null) {
          RawDatagramSocket? boundSocket;
          try {
            boundSocket = await RawDatagramSocket.bind(matchingIp, 0);
            boundSocket.send(data, targetAddr, parsedPort);
          } catch (_) {
          } finally {
            boundSocket?.close();
          }
        }
        _socket?.send(data, targetAddr, parsedPort);
      }
    } catch (_) {
      _socket?.send(data, InternetAddress(ip), parsedPort);
    }
  }

  DiscoveryMessage _createDiscoveryMessage({
    bool connectionRequested = false,
    bool automaticReconnect = false,
    bool? pairingRequired,
    List<String>? addresses,
  }) {
    return DiscoveryMessage(
      version: AppConstants.appVersion,
      deviceId: _localIdentity.deviceId,
      name: _localIdentity.name,
      manufacturer: _localIdentity.manufacturer,
      model: _localIdentity.model,
      platform: _platformName,
      port: sessionPortProvider?.call() ?? AppConstants.sessionTcpPort,
      discoveryPort: _socket?.port ?? _listenPort,
      capabilities: const [
        'file_transfer',
        'clipboard',
        'remote_control',
        'backup',
      ],
      protocolVersion: AppConstants.protocolVersion,
      timestamp: DateTime.now().millisecondsSinceEpoch ~/ 1000,
      connectionRequested: connectionRequested,
      automaticReconnect: automaticReconnect,
      pairingRequired: pairingRequired ?? this.pairingRequired,
      addresses: addresses ?? _cachedPhysicalAddresses,
    );
  }

  String get _platformName {
    if (Platform.isAndroid) return 'android';
    if (Platform.isWindows) return 'windows';
    if (Platform.isLinux) return 'linux';
    if (Platform.isMacOS) return 'macos';
    if (Platform.isIOS) return 'ios';
    return 'unknown';
  }

  void stop() {
    _broadcastTimer?.cancel();
    _broadcastTimer = null;
    _subnetProbeTimer?.cancel();
    _subnetProbeTimer = null;
    _pruneTimer?.cancel();
    _pruneTimer = null;
    _socket?.close();
    _socket = null;
    _isRunning = false;
    _isListening = false;
    _lastPeerReplies.clear();
    _lastConnectionRequests.clear();
    if (Platform.isAndroid) {
      _platform
          .invokeMethod<bool>('releaseDiscoveryMulticastLock')
          .catchError((_) => false);
    }
  }

  void dispose() {
    stop();
    _connectionRequestController.close();
    _registry.dispose();
  }
}

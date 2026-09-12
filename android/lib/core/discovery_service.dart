import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart';

import 'constants.dart';
import 'device_identity_manager.dart';
import 'device_registry.dart';
import 'discovery_message.dart';

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
    final socket = _socket;
    if (socket == null) {
      _lastError = '设备发现服务没有可用的 UDP socket';
      return;
    }

    bool sent = false;
    String? lastSendError;
    try {
      final message = _createDiscoveryMessage();
      final data = utf8.encode(jsonEncode(message.toJson()));
      final targets = <InternetAddress>[InternetAddress('255.255.255.255')];
      final interfaces = await NetworkInterface.list(
        type: InternetAddressType.IPv4,
        includeLoopback: false,
        includeLinkLocal: false,
      );
      for (final networkInterface in interfaces) {
        for (final interfaceAddress in networkInterface.addresses) {
          final broadcast = interfaceAddress.broadcast;
          if (broadcast != null &&
              !targets.any((target) => target.address == broadcast.address)) {
            targets.add(broadcast);
          }
        }
      }

      for (final target in targets) {
        try {
          final bytesSent = socket.send(data, target, _listenPort);
          sent = sent || bytesSent > 0;
        } catch (error) {
          // One blocked broadcast target must not prevent the directed
          // broadcast addresses of the other active adapters from running.
          lastSendError = '$error';
        }
      }
    } catch (error) {
      lastSendError = '$error';
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
    final socket = _socket;
    if (socket == null) return;

    try {
      final interfaces = await NetworkInterface.list(
        type: InternetAddressType.IPv4,
        includeLoopback: false,
        includeLinkLocal: false,
      );
      final localAddresses = <String>{};
      var sent = false;
      final data = utf8.encode(jsonEncode(_createDiscoveryMessage().toJson()));

      for (final networkInterface in interfaces) {
        for (final interfaceAddress in networkInterface.addresses) {
          final octets = _ipv4Octets(interfaceAddress.address);
          if (octets == null || interfaceAddress.prefixLength <= 0) continue;
          final local = octets.join('.');
          localAddresses.add(local);

          // Limit the fallback sweep to the /24 containing this device. This
          // avoids scanning an entire /16 while still covering normal home
          // and home or enterprise Wi-Fi networks.
          final prefix = interfaceAddress.prefixLength < 24
              ? 24
              : interfaceAddress.prefixLength;
          if (prefix > 30) continue;
          final base = _networkBase(octets, prefix);
          final hostCount = 1 << (32 - prefix);
          for (var host = 1; host < hostCount - 1; host++) {
            final target = '${base[0]}.${base[1]}.${base[2]}.$host';
            if (localAddresses.contains(target)) continue;
            try {
              sent =
                  socket.send(data, InternetAddress(target), _listenPort) > 0 ||
                  sent;
            } catch (_) {
              // A host may be offline or filtered; continue probing others.
            }
          }
        }
      }

      if (sent && _isListening && _lastError?.startsWith('无法发送') == true) {
        _lastError = null;
      }
    } catch (error) {
      _lastError = '局域网定向探测失败：$error';
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

  void probeManualIp(String ip, [int port = AppConstants.discoveryUdpPort]) {
    if (_socket == null) return;
    try {
      final message = _createDiscoveryMessage();
      final data = utf8.encode(jsonEncode(message.toJson()));
      _socket?.send(
        data,
        InternetAddress(ip),
        parseNetworkPort(port, AppConstants.discoveryUdpPort),
      );
    } catch (_) {}
  }

  /// Asks the peer to open the TCP session in the opposite direction.
  /// UDP discovery is often reachable even when a renamed Windows executable
  /// has not yet been granted an inbound TCP firewall exception.
  void requestReverseConnection(
    String ip, [
    int port = AppConstants.discoveryUdpPort,
    bool automaticReconnect = false,
  ]) {
    if (_socket == null) return;
    try {
      final message = _createDiscoveryMessage(
        connectionRequested: true,
        automaticReconnect: automaticReconnect,
      );
      final data = utf8.encode(jsonEncode(message.toJson()));
      _socket?.send(
        data,
        InternetAddress(ip),
        parseNetworkPort(port, AppConstants.discoveryUdpPort),
      );
    } catch (_) {}
  }

  DiscoveryMessage _createDiscoveryMessage({
    bool connectionRequested = false,
    bool automaticReconnect = false,
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

import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/services.dart';

import 'constants.dart';
import 'device_identity_manager.dart';
import 'network_interface_helper.dart';
import 'pairing_manager.dart';
import 'protocol_compression.dart';
import 'protocol_frame.dart';
import 'trust_store.dart';

enum SessionState {
  disconnected,
  connecting,
  authenticating,
  connected,
  suspended,
  reconnecting,
}

extension SessionStateX on SessionState {
  /// The authenticated transport is still owned by the native service while
  /// Android is temporarily not delivering LAN traffic (typically Doze).
  bool get isUsable =>
      this == SessionState.connected || this == SessionState.suspended;
}

class SessionPeerInfo {
  final String deviceId;
  final String name;
  final String manufacturer;
  final String model;
  final String platform;
  final Set<String> capabilities;
  final bool pairingRequired;

  const SessionPeerInfo({
    required this.deviceId,
    required this.name,
    this.manufacturer = '',
    this.model = '',
    required this.platform,
    this.capabilities = const <String>{},
    this.pairingRequired = false,
  });
}

/// Transport-neutral session API shared by the ordinary Dart socket and the
/// Android foreground-service transport. Keeping this surface stable lets the
/// feature managers (files, clipboard, notifications and workspace commands)
/// continue to consume protocol frames while the socket lifecycle moves out
/// of the Android UI isolate.
abstract class SessionConnection {
  SessionConnection._();

  factory SessionConnection({
    required Socket socket,
    required DeviceIdentity localIdentity,
    bool? isOutbound,
    bool? startImmediately,
    String? localPairingCode,
    String? remotePairingCode,
  }) = _SocketSessionConnection;

  SessionState get state;
  bool get isDisposed;
  bool get isReady;
  Stream<ProtocolFrame> get frames;
  Stream<SessionState> get stateStream;
  Stream<SessionPeerInfo> get peerStream;
  SessionPeerInfo? get peerInfo;
  DeviceIdentity get localIdentity;
  String get remoteAddress;
  bool get peerRequiresPairing;
  bool get isPairingAuthenticated;
  String? get pairingError;
  bool get isOutbound;

  Future<bool> waitUntilReady({Duration timeout = const Duration(seconds: 3)});

  void start();
  void sendFrame(MessageType type, Uint8List payload);
  void sendJson(MessageType type, Map<String, dynamic> json);

  /// Closes this connection.
  ///
  /// `manual` is false for lifecycle cleanup (for example, losing a
  /// duplicate race or abandoning a failed connection attempt). Only an
  /// explicit user disconnect should suppress the native service's historical
  /// reconnect policy.
  void dispose({bool manual = true});
}

class _SocketSessionConnection extends SessionConnection {
  static const int _maxMissedHeartbeats = 6;
  final Socket _socket;
  final DeviceIdentity _localIdentity;
  final String _localPairingCode;
  final String _remotePairingCode;
  final String _localPairingChallenge = PairingManager.createChallenge();
  @override
  final bool isOutbound;
  final StreamController<ProtocolFrame> _frameController =
      StreamController<ProtocolFrame>.broadcast();
  final StreamController<SessionState> _stateController =
      StreamController<SessionState>.broadcast();
  final StreamController<SessionPeerInfo> _peerController =
      StreamController<SessionPeerInfo>.broadcast();

  final List<int> _incomingBuffer = [];
  Timer? _heartbeatTimer;
  int _missedHeartbeats = 0;
  SessionState _state = SessionState.connecting;
  SessionPeerInfo? _peerInfo;
  bool _disposed = false;
  bool _started = false;
  bool _peerPairingRequired = false;
  bool _peerPairingVerified = false;
  bool _pairingProofSent = false;
  String _peerPairingChallenge = '';
  String? _pairingError;
  final bool startImmediately;
  final List<int> _outgoingBuffer = [];
  bool _writeInProgress = false;

  @override
  SessionState get state => _state;
  @override
  bool get isDisposed => _disposed;
  @override
  bool get isReady => !_disposed && _state.isUsable;
  @override
  Stream<ProtocolFrame> get frames => _frameController.stream;
  @override
  Stream<SessionState> get stateStream => _stateController.stream;
  @override
  Stream<SessionPeerInfo> get peerStream => _peerController.stream;
  @override
  SessionPeerInfo? get peerInfo => _peerInfo;
  @override
  DeviceIdentity get localIdentity => _localIdentity;
  @override
  String get remoteAddress => _socket.remoteAddress.address;
  @override
  bool get peerRequiresPairing => _peerPairingRequired;
  @override
  bool get isPairingAuthenticated =>
      _peerPairingVerified && (!_peerPairingRequired || _pairingProofSent);
  @override
  String? get pairingError => _pairingError;

  /// Waits for the identity handshake to complete before a feature sends its
  /// first frame. This closes the small race where discovery has created a
  /// socket but the peer has not yet replied to SessionInit.
  @override
  Future<bool> waitUntilReady({
    Duration timeout = const Duration(seconds: 3),
  }) async {
    if (isReady) return true;
    if (_disposed) return false;
    try {
      await stateStream
          .firstWhere(
            (state) => state.isUsable || state == SessionState.disconnected,
          )
          .timeout(timeout);
    } catch (_) {
      return isReady;
    }
    return isReady;
  }

  _SocketSessionConnection({
    required this._socket,
    required this._localIdentity,
    bool? isOutbound,
    bool? startImmediately,
    String? localPairingCode,
    String? remotePairingCode,
  }) : isOutbound = isOutbound ?? false,
       startImmediately = startImmediately ?? true,
       _localPairingCode = PairingManager.normalizePairingCode(
         localPairingCode,
       ),
       _remotePairingCode = PairingManager.normalizePairingCode(
         remotePairingCode,
       ),
       super._() {
    try {
      _socket.setOption(SocketOption.tcpNoDelay, true);
    } catch (_) {}
    if (this.startImmediately) start();
  }

  /// Starts network I/O after the session manager has attached its lifecycle,
  /// peer-identity and command observers. A fast peer must not be able to send
  /// the first SessionInit before those observers exist.
  @override
  void start() {
    if (_started || _disposed) return;
    _started = true;
    _state = SessionState.authenticating;
    _socket.listen(
      _onData,
      onError: (_) => _updateState(SessionState.disconnected),
      onDone: () => _updateState(SessionState.disconnected),
      cancelOnError: true,
    );
    _sendSessionIdentity(MessageType.sessionInit);
    _heartbeatTimer = Timer.periodic(
      const Duration(seconds: 5),
      (_) => _sendHeartbeat(),
    );
    unawaited(_authenticationTimeout());
  }

  @override
  void sendFrame(MessageType type, Uint8List payload) {
    if (_disposed) return;
    try {
      var frameType = type;
      var framePayload = payload;
      if (_peerInfo?.capabilities.contains(ProtocolCompression.capability) ==
          true) {
        final compressed = ProtocolCompression.tryCompress(type, payload);
        if (compressed != null) {
          frameType = MessageType.compressedControl;
          framePayload = compressed;
        }
      }
      _outgoingBuffer.addAll(
        ProtocolFrame(type: frameType, payload: framePayload).serialize(),
      );
      _flushOutgoing();
    } catch (_) {
      _updateState(SessionState.disconnected);
    }
  }

  void _flushOutgoing() {
    if (_writeInProgress || _disposed || _outgoingBuffer.isEmpty) return;
    _writeInProgress = true;
    final bytes = Uint8List.fromList(_outgoingBuffer);
    _outgoingBuffer.clear();
    try {
      _socket.add(bytes);
      // Socket.add queues bytes directly; flushing every frame adds latency
      // to bulk FILE_CHUNK traffic without making a TCP socket more reliable.
      _writeInProgress = false;
      if (_outgoingBuffer.isNotEmpty) _flushOutgoing();
    } catch (_) {
      _writeInProgress = false;
      _updateState(SessionState.disconnected);
    }
  }

  @override
  void sendJson(MessageType type, Map<String, dynamic> json) {
    sendFrame(type, Uint8List.fromList(utf8.encode(jsonEncode(json))));
  }

  void _sendSessionIdentity(MessageType type) {
    sendJson(type, {
      'deviceId': _localIdentity.deviceId,
      'name': _localIdentity.name,
      'manufacturer': _localIdentity.manufacturer,
      'model': _localIdentity.model,
      'platform': _platformName,
      'capabilities': <String>[ProtocolCompression.capability],
      'pairingRequired': _localPairingCode.isNotEmpty,
      'pairingChallenge': _localPairingChallenge,
      'pairingProof': '',
    });
  }

  Future<void> _sendSessionAck() async {
    final proof = PairingManager.createProof(
      _remotePairingCode,
      _peerPairingChallenge,
    );
    if (_peerPairingRequired && proof.isNotEmpty) _pairingProofSent = true;
    sendJson(MessageType.sessionAck, {
      'deviceId': _localIdentity.deviceId,
      'name': _localIdentity.name,
      'manufacturer': _localIdentity.manufacturer,
      'model': _localIdentity.model,
      'platform': _platformName,
      'capabilities': <String>[ProtocolCompression.capability],
      'pairingRequired': _localPairingCode.isNotEmpty,
      'pairingChallenge': _localPairingChallenge,
      'pairingProof': proof,
    });
    _tryCompleteAuthentication();
  }

  String get _platformName {
    if (Platform.isAndroid) return 'android';
    if (Platform.isWindows) return 'windows';
    if (Platform.isLinux) return 'linux';
    if (Platform.isMacOS) return 'macos';
    if (Platform.isIOS) return 'ios';
    return 'unknown';
  }

  void _sendHeartbeat() {
    if (_state != SessionState.connected) return;
    _missedHeartbeats++;
    if (_missedHeartbeats > _maxMissedHeartbeats) {
      _updateState(SessionState.reconnecting);
      dispose();
      return;
    }
    sendFrame(MessageType.heartbeatPing, Uint8List(0));
  }

  void _onData(Uint8List chunk) {
    _incomingBuffer.addAll(chunk);
    while (_incomingBuffer.length >= ProtocolFrame.headerSize) {
      final headerBytes = Uint8List.fromList(
        _incomingBuffer.sublist(0, ProtocolFrame.headerSize),
      );
      final payloadLen = ByteData.sublistView(headerBytes)
          .getUint32(48, Endian.big);
      if (payloadLen > ProtocolFrame.maxPayloadSize) {
        _updateState(SessionState.disconnected);
        return;
      }
      if (_incomingBuffer.length < ProtocolFrame.headerSize + payloadLen) {
        break;
      }

      final frameLength = ProtocolFrame.headerSize + payloadLen;
      final fullFrameBytes = Uint8List.fromList(
        _incomingBuffer.sublist(0, frameLength),
      );
      _incomingBuffer.removeRange(0, frameLength);
      final frame = ProtocolFrame.tryParse(fullFrameBytes);
      if (frame != null) _handleFrame(frame);
    }
  }

  void _handleFrame(ProtocolFrame frame) {
    if (frame.type == MessageType.sessionInit ||
        frame.type == MessageType.sessionAck) {
      _acceptPeerIdentity(frame.payload);
      if (frame.type == MessageType.sessionInit) {
        unawaited(_sendSessionAck());
      }
      return;
    }
    if (frame.type == MessageType.heartbeatPing) {
      sendFrame(MessageType.heartbeatPong, Uint8List(0));
      return;
    }
    if (frame.type == MessageType.heartbeatPong) {
      _missedHeartbeats = 0;
      return;
    }
    if (frame.type == MessageType.compressedControl) {
      final decompressed = ProtocolCompression.tryDecompress(frame.payload);
      if (decompressed == null) return;
      _frameController.add(
        ProtocolFrame(
          version: frame.version,
          type: decompressed.type,
          messageId: frame.messageId,
          timestamp: frame.timestamp,
          sessionId: frame.sessionId,
          payload: decompressed.payload,
        ),
      );
      return;
    }
    _frameController.add(frame);
  }

  void _acceptPeerIdentity(Uint8List payload) {
    try {
      final json = jsonDecode(utf8.decode(payload)) as Map;
      final deviceId = '${json['deviceId'] ?? ''}';
      if (deviceId.isEmpty || deviceId == _localIdentity.deviceId) return;
      final peer = SessionPeerInfo(
        deviceId: deviceId,
        name: '${json['name'] ?? '未命名设备'}',
        manufacturer: '${json['manufacturer'] ?? ''}',
        model: '${json['model'] ?? ''}',
        platform: '${json['platform'] ?? 'unknown'}',
        capabilities: _readCapabilities(json['capabilities']),
        pairingRequired: json['pairingRequired'] == true,
      );
      _peerPairingRequired = peer.pairingRequired;
      final challenge = '${json['pairingChallenge'] ?? ''}'.trim();
      if (challenge.isNotEmpty) {
        _peerPairingChallenge = challenge.toLowerCase();
      }
      final proof = '${json['pairingProof'] ?? ''}';
      _peerPairingVerified =
          _localPairingCode.isEmpty ||
          PairingManager.verifyProof(
            _localPairingCode,
            _localPairingChallenge,
            proof,
          );
      if (_localPairingCode.isNotEmpty && !_peerPairingVerified) {
        _pairingError = '本机已设置配对码，但对方未提供正确配对码。';
      } else if (_peerPairingRequired && _remotePairingCode.isEmpty) {
        _pairingError = '目标设备需要输入 6 位配对码。';
      }
      if (_peerInfo?.deviceId == peer.deviceId) {
        // The peer may repeat SessionInit/SessionAck while both sides are
        // reconnecting. Do not leave a valid socket in the authenticating
        // state just because the identity payload did not change.
        _peerInfo = peer;
        _tryCompleteAuthentication();
        return;
      }
      _peerInfo = peer;
      _tryCompleteAuthentication();
    } catch (_) {
      // Ignore malformed identity frames without dropping the socket.
    }
  }

  void _tryCompleteAuthentication() {
    if (_peerInfo == null || _disposed || _state == SessionState.connected) {
      return;
    }
    if (_localPairingCode.isNotEmpty && !_peerPairingVerified) return;
    if (_peerPairingRequired && !_pairingProofSent) {
      _pairingError ??= '目标设备需要输入 6 位配对码。';
      return;
    }
    _pairingError = null;
    _updateState(SessionState.connected);
    if (!_peerController.isClosed) _peerController.add(_peerInfo!);
  }

  Future<void> _authenticationTimeout() async {
    try {
      await Future<void>.delayed(const Duration(seconds: 8));
      if (_state != SessionState.connected && !_disposed) {
        _pairingError ??= '设备身份或配对码验证超时。';
        dispose();
      }
    } catch (_) {}
  }

  static Set<String> _readCapabilities(Object? raw) {
    if (raw is! List) return const <String>{};
    return raw
        .whereType<Object>()
        .map((value) => '$value'.trim())
        .where((value) => value.isNotEmpty)
        .toSet();
  }

  void _updateState(SessionState newState) {
    if (_state == newState) return;
    _state = newState;
    if (!_stateController.isClosed) _stateController.add(newState);
    if (newState == SessionState.disconnected) dispose();
  }

  @override
  void dispose({bool manual = true}) {
    if (_disposed) return;
    _disposed = true;
    _heartbeatTimer?.cancel();
    _heartbeatTimer = null;
    if (_state != SessionState.disconnected && !_stateController.isClosed) {
      _state = SessionState.disconnected;
      _stateController.add(SessionState.disconnected);
    }
    try {
      _socket.destroy();
    } catch (_) {}
    _frameController.close();
    _stateController.close();
    _peerController.close();
  }
}

/// Method/EventChannel bridge for the Android foreground connection service.
/// The service owns the actual TCP sockets; this bridge only carries session
/// state and protocol frames to the Flutter feature layer.
class _NativeSessionBridge {
  static const MethodChannel _methodChannel = MethodChannel('hinge/platform');
  static const EventChannel _eventChannel = EventChannel(
    'hinge/native_connection/events',
  );

  Stream<Map<String, dynamic>> get events => _eventChannel
      .receiveBroadcastStream()
      .where((event) => event is Map)
      .map((event) => Map<String, dynamic>.from(event as Map));

  Future<bool> startSession({
    required DeviceIdentity identity,
    required String localPairingCode,
    required int listenPort,
  }) async {
    final result = await _methodChannel.invokeMethod<dynamic>(
      'startNativeSession',
      {
        'deviceId': identity.deviceId,
        'name': identity.name,
        'manufacturer': identity.manufacturer,
        'model': identity.model,
        'localPairingCode': localPairingCode,
        'listenPort': listenPort,
      },
    );
    return result == true;
  }

  Future<void> updateLocalPairingCode(String pairingCode) async {
    await _methodChannel.invokeMethod<void>('updateNativePairingCode', {
      'localPairingCode': pairingCode,
    });
  }

  Future<String> connect({
    required String connectionId,
    required String address,
    required int port,
    required String remotePairingCode,
  }) async {
    final result = await _methodChannel.invokeMethod<dynamic>('nativeConnect', {
      'connectionId': connectionId,
      'address': address,
      'port': port,
      'remotePairingCode': remotePairingCode,
    });
    final value = '$result'.trim();
    if (value.isEmpty) throw StateError('原生连接服务未返回会话标识');
    return value;
  }

  Future<void> disconnect(String connectionId, {bool manual = true}) async {
    await _methodChannel.invokeMethod<void>('nativeDisconnect', {
      'connectionId': connectionId,
      'manual': manual,
    });
  }

  Future<void> sendFrame(
    String connectionId,
    MessageType type,
    Uint8List payload,
  ) async {
    await _methodChannel.invokeMethod<void>('nativeSendFrame', {
      'connectionId': connectionId,
      'type': type.value,
      'payload': payload,
    });
  }

  Future<bool> enqueueFile({
    required String path,
    required String name,
    required String mimeType,
    required String targetDeviceId,
  }) async {
    final result = await _methodChannel.invokeMethod<dynamic>(
      'nativeEnqueueFile',
      {
        'path': path,
        'name': name,
        'mimeType': mimeType,
        'targetDeviceId': targetDeviceId,
        'deleteAfter': true,
      },
    );
    return result == true;
  }
}

class _NativeSessionConnection extends SessionConnection {
  final _NativeSessionBridge _bridge;
  final String connectionId;
  final DeviceIdentity _localIdentity;
  @override
  final bool isOutbound;
  final StreamController<ProtocolFrame> _frameController =
      StreamController<ProtocolFrame>.broadcast();
  final StreamController<SessionState> _stateController =
      StreamController<SessionState>.broadcast();
  final StreamController<SessionPeerInfo> _peerController =
      StreamController<SessionPeerInfo>.broadcast();

  SessionState _state;
  SessionPeerInfo? _peerInfo;
  @override
  String remoteAddress;
  bool _disposed = false;
  bool _peerRequiresPairing = false;
  bool _pairingAuthenticated = false;
  String? _pairingError;

  _NativeSessionConnection({
    required this._bridge,
    required this.connectionId,
    required this._localIdentity,
    required this.isOutbound,
    this.remoteAddress = '',
  }) : _state = SessionState.connecting,
       super._();

  @override
  SessionState get state => _state;

  @override
  bool get isDisposed => _disposed;

  @override
  bool get isReady => !_disposed && _state.isUsable;

  @override
  Stream<ProtocolFrame> get frames => _frameController.stream;

  @override
  Stream<SessionState> get stateStream => _stateController.stream;

  @override
  Stream<SessionPeerInfo> get peerStream => _peerController.stream;

  @override
  SessionPeerInfo? get peerInfo => _peerInfo;

  @override
  DeviceIdentity get localIdentity => _localIdentity;

  @override
  bool get peerRequiresPairing => _peerRequiresPairing;

  @override
  bool get isPairingAuthenticated => _pairingAuthenticated;

  @override
  String? get pairingError => _pairingError;

  @override
  Future<bool> waitUntilReady({
    Duration timeout = const Duration(seconds: 3),
  }) async {
    if (isReady) return true;
    if (_disposed) return false;
    try {
      await stateStream
          .firstWhere(
            (value) => value.isUsable || value == SessionState.disconnected,
          )
          .timeout(timeout);
    } catch (_) {
      return isReady;
    }
    return isReady;
  }

  @override
  void start() {}

  @override
  void sendFrame(MessageType type, Uint8List payload) {
    if (_disposed) return;
    var frameType = type;
    var framePayload = payload;
    if (_peerInfo?.capabilities.contains(ProtocolCompression.capability) ==
        true) {
      final compressed = ProtocolCompression.tryCompress(type, payload);
      if (compressed != null) {
        frameType = MessageType.compressedControl;
        framePayload = compressed;
      }
    }
    unawaited(_bridge.sendFrame(connectionId, frameType, framePayload));
  }

  @override
  void sendJson(MessageType type, Map<String, dynamic> json) {
    sendFrame(type, Uint8List.fromList(utf8.encode(jsonEncode(json))));
  }

  void handleEvent(Map<String, dynamic> event) {
    if (_disposed) return;
    final eventType = '${event['event'] ?? ''}';
    switch (eventType) {
      case 'peer':
        _handlePeer(event);
        break;
      case 'state':
        _handleState(event);
        break;
      case 'frame':
        _handleFrame(event);
        break;
    }
  }

  void _handlePeer(Map<String, dynamic> event) {
    final deviceId = '${event['deviceId'] ?? ''}'.trim();
    if (deviceId.isEmpty) return;
    remoteAddress = '${event['remoteAddress'] ?? remoteAddress}';
    _peerRequiresPairing = event['pairingRequired'] == true;
    _pairingAuthenticated = event['pairingAuthenticated'] == true;
    _pairingError = '${event['pairingError'] ?? ''}'.trim();
    if (_pairingError?.isEmpty == true) _pairingError = null;
    final peer = SessionPeerInfo(
      deviceId: deviceId,
      name: '${event['name'] ?? '未命名设备'}',
      manufacturer: '${event['manufacturer'] ?? ''}',
      model: '${event['model'] ?? ''}',
      platform: '${event['platform'] ?? 'unknown'}',
      capabilities: _readCapabilities(event['capabilities']),
      pairingRequired: _peerRequiresPairing,
    );
    _peerInfo = peer;
    if (!_peerController.isClosed) _peerController.add(peer);
  }

  void _handleState(Map<String, dynamic> event) {
    final raw = '${event['state'] ?? 'disconnected'}'.toLowerCase();
    final next = switch (raw) {
      'connecting' => SessionState.connecting,
      'authenticating' => SessionState.authenticating,
      'connected' => SessionState.connected,
      'suspended' => SessionState.suspended,
      'reconnecting' => SessionState.reconnecting,
      _ => SessionState.disconnected,
    };
    if (event.containsKey('remoteAddress')) {
      remoteAddress = '${event['remoteAddress'] ?? remoteAddress}';
    }
    if (event['pairingError'] != null) {
      _pairingError = '${event['pairingError']}'.trim();
    }
    if (_state == next) return;
    _state = next;
    if (!_stateController.isClosed) _stateController.add(next);
    if (next == SessionState.disconnected) {
      _closeStreams();
    }
  }

  void _handleFrame(Map<String, dynamic> event) {
    final type = MessageType.fromValue(
      (event['type'] as num?)?.toInt() ?? MessageType.unknown.value,
    );
    final payload = _bytes(event['payload']);
    final frame = ProtocolFrame(
      version: (event['version'] as num?)?.toInt() ?? 1,
      type: type,
      messageId: _bytes(event['messageId'], length: 16),
      timestamp: (event['timestamp'] as num?)?.toInt(),
      sessionId: _bytes(event['sessionId'], length: 16),
      payload: payload,
    );
    if (type == MessageType.compressedControl) {
      final decompressed = ProtocolCompression.tryDecompress(payload);
      if (decompressed == null) return;
      if (!_frameController.isClosed) {
        _frameController.add(
          ProtocolFrame(
            version: frame.version,
            type: decompressed.type,
            messageId: frame.messageId,
            timestamp: frame.timestamp,
            sessionId: frame.sessionId,
            payload: decompressed.payload,
          ),
        );
      }
      return;
    }
    if (!_frameController.isClosed) _frameController.add(frame);
  }

  static Uint8List _bytes(Object? value, {int? length}) {
    final bytes = value is Uint8List
        ? Uint8List.fromList(value)
        : value is List
        ? Uint8List.fromList(
            value.whereType<num>().map((v) => v.toInt()).toList(),
          )
        : Uint8List(0);
    if (length == null || bytes.length == length) return bytes;
    final result = Uint8List(length);
    final copyLength = bytes.length < length ? bytes.length : length;
    result.setRange(0, copyLength, bytes, 0);
    return result;
  }

  static Set<String> _readCapabilities(Object? raw) {
    if (raw is! List) return const <String>{};
    return raw
        .whereType<Object>()
        .map((value) => '$value'.trim())
        .where((value) => value.isNotEmpty)
        .toSet();
  }

  @override
  void dispose({bool manual = true}) {
    if (_disposed) return;
    _disposed = true;
    unawaited(_bridge.disconnect(connectionId, manual: manual));
    _closeStreams();
  }

  void _closeStreams() {
    if (!_stateController.isClosed && _state != SessionState.disconnected) {
      _state = SessionState.disconnected;
      _stateController.add(SessionState.disconnected);
    }
    if (!_frameController.isClosed) _frameController.close();
    if (!_stateController.isClosed) _stateController.close();
    if (!_peerController.isClosed) _peerController.close();
  }
}

class SessionManager {
  // Android's foreground service owns the long-lived transport so an Activity
  // or Flutter isolate suspension does not tear down the LAN session.
  static const bool _enableAndroidNativeTransport = true;
  final DeviceIdentity _localIdentity;
  final TrustStore _trustStore;
  final int _listenPort;
  late final _NativeSessionBridge? _nativeBridge;
  StreamSubscription<Map<String, dynamic>>? _nativeEventSubscription;
  final Map<String, _NativeSessionConnection> _nativeConnections = {};
  final Map<String, List<Map<String, dynamic>>> _pendingNativeEvents = {};
  String _localPairingCode = '';
  ServerSocket? _serverSocket;
  final List<SessionConnection> _connections = [];
  final StreamController<SessionConnection> _connectionController =
      StreamController<SessionConnection>.broadcast();
  // This stream includes both incoming and outgoing sessions.  The command
  // router must observe both directions: a phone can initiate the TCP
  // connection, after which Windows still needs to be able to request files,
  // photos, calendar data, notes and tasks on that same session.
  final StreamController<SessionConnection> _connectionCreatedController =
      StreamController<SessionConnection>.broadcast();
  final StreamController<void> _networkPolicyController =
      StreamController<void>.broadcast();
  bool _isListening = false;
  String? _lastError;

  Stream<SessionConnection> get onClientConnected =>
      _connectionController.stream;
  Stream<SessionConnection> get onConnectionCreated =>
      _connectionCreatedController.stream;
  Stream<void> get onNetworkPolicyChanged => _networkPolicyController.stream;
  bool get isListening => _isListening;
  String? get lastError => _lastError;
  int get listeningPort => _serverSocket?.port ?? _listenPort;
  DeviceIdentity get localIdentity => _localIdentity;
  TrustStore get trustStore => _trustStore;
  String get localPairingCode => _localPairingCode;
  bool get usesNativeTransport => _nativeBridge != null;

  SessionManager({
    required this._localIdentity,
    required this._trustStore,
    this._listenPort = AppConstants.sessionTcpPort,
    String? localPairingCode,
  }) {
    _localPairingCode = PairingManager.normalizePairingCode(localPairingCode);
    _nativeBridge =
        _enableAndroidNativeTransport &&
            Platform.isAndroid &&
            !Platform.environment.containsKey('FLUTTER_TEST')
        ? _NativeSessionBridge()
        : null;
    _nativeEventSubscription = _nativeBridge?.events.listen(_handleNativeEvent);
  }

  set localPairingCode(String value) {
    _localPairingCode = PairingManager.normalizePairingCode(value);
    final nativeBridge = _nativeBridge;
    if (_isListening && nativeBridge != null) {
      unawaited(nativeBridge.updateLocalPairingCode(_localPairingCode));
    }
  }

  SessionConnection? connectionForDevice(String deviceId) {
    for (final connection in _connections) {
      if (!connection.isDisposed &&
          connection.state.isUsable &&
          connection.peerInfo?.deviceId == deviceId) {
        return connection;
      }
    }
    return null;
  }

  Future<void> startListener() async {
    final nativeBridge = _nativeBridge;
    if (nativeBridge != null) {
      if (_isListening) return;
      try {
        final started = await nativeBridge.startSession(
          identity: _localIdentity,
          localPairingCode: _localPairingCode,
          listenPort: _listenPort,
        );
        _isListening = started;
        _lastError = started ? null : 'Android 原生连接服务未能启动';
      } catch (error) {
        _isListening = false;
        _lastError = 'Android 原生连接服务启动失败：$error';
      }
      return;
    }
    if (_serverSocket != null || _isListening) return;
    try {
      _serverSocket = await ServerSocket.bind(
        InternetAddress.anyIPv4,
        _listenPort,
        shared: false,
      );
      _serverSocket?.listen(_onIncomingSocket);
      _isListening = true;
      _lastError = null;
    } catch (e, st) {
      // A stale process or an OEM clone of the app can still own the fixed
      // port after an update. Use an ephemeral port and advertise it through
      // discovery instead of leaving the phone unreachable altogether.
      try {
        _serverSocket = await ServerSocket.bind(
          InternetAddress.anyIPv4,
          0,
          shared: false,
        );
        _serverSocket?.listen(_onIncomingSocket);
        _isListening = true;
        _lastError = '标准端口 $_listenPort 被占用，已切换到临时端口 ${_serverSocket?.port}';
      } catch (fallbackError, fallbackStack) {
        _isListening = false;
        _lastError = '无法监听 TCP $_listenPort：$fallbackError';
        // ignore: avoid_print
        print(
          'ServerSocket.bind error: $e\n$st\nFallback error: $fallbackError\n$fallbackStack',
        );
      }
    }
  }

  Future<SessionConnection> connectToPeer(
    InternetAddress address, [
    int port = AppConstants.sessionTcpPort,
    String? remotePairingCode,
  ]) async {
    final nativeBridge = _nativeBridge;
    if (nativeBridge != null) {
      final connectionId = _nativeConnectionId();
      final connection = _NativeSessionConnection(
        bridge: nativeBridge,
        connectionId: connectionId,
        localIdentity: _localIdentity,
        isOutbound: true,
        remoteAddress: address.address,
      );
      _registerNativeConnection(connection);
      try {
        await nativeBridge.connect(
          connectionId: connectionId,
          address: address.address,
          port: port,
          remotePairingCode: PairingManager.normalizePairingCode(
            remotePairingCode,
          ),
        );
        // Preserve the Socket.connect contract used by the caller: completing
        // this Future means the session is authenticated and usable, not just
        // that a native worker was queued. Without this wait, the first stale
        // address looked successful and prevented fallback to the remaining
        // LAN addresses before its asynchronous failure arrived.
        final ready = await connection.waitUntilReady(
          timeout: const Duration(seconds: 4),
        );
        if (!ready) {
          final reason = connection.pairingError ?? '原生会话连接失败';
          connection.dispose(manual: false);
          throw SocketException(reason, address: address, port: port);
        }
        return connection;
      } catch (_) {
        connection.dispose(manual: false);
        rethrow;
      }
    }
    final matchingLocalIp =
        await NetworkInterfaceHelper.findMatchingLocalPhysicalAddress(address);
    if (matchingLocalIp != null) {
      try {
        final boundSocket = await Socket.connect(
          address,
          port,
          sourceAddress: matchingLocalIp.address,
          timeout: const Duration(seconds: 3),
        );
        return _registerConnection(
          boundSocket,
          isOutbound: true,
          remotePairingCode: remotePairingCode,
        );
      } catch (_) {
        // Fall back to unbound connection below
      }
    }

    final socket = await Socket.connect(
      address,
      port,
      timeout: const Duration(seconds: 3),
    );
    return _registerConnection(
      socket,
      isOutbound: true,
      remotePairingCode: remotePairingCode,
    );
  }

  void _onIncomingSocket(Socket socket) {
    final connection = _registerConnection(
      socket,
      isOutbound: false,
      remotePairingCode: null,
    );
    _connectionController.add(connection);
  }

  SessionConnection _registerConnection(
    Socket socket, {
    required bool isOutbound,
    required String? remotePairingCode,
  }) {
    final connection = SessionConnection(
      socket: socket,
      localIdentity: _localIdentity,
      isOutbound: isOutbound,
      startImmediately: false,
      localPairingCode: _localPairingCode,
      remotePairingCode: remotePairingCode,
    );
    _connections.add(connection);
    _connectionCreatedController.add(connection);
    connection.peerStream.listen(
      (_) => _removeDuplicateConnections(connection),
    );
    connection.stateStream.listen((state) {
      if (state == SessionState.disconnected) _connections.remove(connection);
    });
    scheduleMicrotask(() {
      if (connection.peerInfo != null) _removeDuplicateConnections(connection);
    });
    connection.start();
    return connection;
  }

  void _registerNativeConnection(_NativeSessionConnection connection) {
    if (_nativeConnections.containsKey(connection.connectionId)) return;
    _nativeConnections[connection.connectionId] = connection;
    _connections.add(connection);
    _connectionCreatedController.add(connection);
    if (!connection.isOutbound) _connectionController.add(connection);
    connection.peerStream.listen(
      (_) => _removeDuplicateConnections(connection),
    );
    connection.stateStream.listen((state) {
      if (state != SessionState.disconnected) return;
      _nativeConnections.remove(connection.connectionId);
      _connections.remove(connection);
    });
  }

  void _handleNativeEvent(Map<String, dynamic> event) {
    final connectionId = '${event['connectionId'] ?? ''}'.trim();
    final eventType = '${event['event'] ?? ''}';
    if (connectionId.isEmpty) {
      if (eventType == 'network_policy_changed' &&
          !_networkPolicyController.isClosed) {
        _networkPolicyController.add(null);
      }
      return;
    }
    var connection = _nativeConnections[connectionId];
    if (eventType == 'connection_created' && connection == null) {
      final nativeBridge = _nativeBridge;
      if (nativeBridge == null) return;
      connection = _NativeSessionConnection(
        bridge: nativeBridge,
        connectionId: connectionId,
        localIdentity: _localIdentity,
        isOutbound: event['isOutbound'] == true,
        remoteAddress: '${event['remoteAddress'] ?? ''}',
      );
      _registerNativeConnection(connection);
      final pending =
          _pendingNativeEvents.remove(connectionId) ??
          const <Map<String, dynamic>>[];
      for (final pendingEvent in pending) {
        connection.handleEvent(pendingEvent);
      }
    }
    if (connection == null) {
      final pending = _pendingNativeEvents.putIfAbsent(
        connectionId,
        () => <Map<String, dynamic>>[],
      );
      if (pending.length >= 128) pending.removeAt(0);
      pending.add(event);
      return;
    }
    connection.handleEvent(event);
    if (eventType == 'peer' || eventType == 'state') {
      _removeDuplicateConnections(connection);
    }
  }

  String _nativeConnectionId() =>
      'flutter-${DateTime.now().microsecondsSinceEpoch}-'
      '${_connections.length}';

  Future<bool> enqueueFileTransfer({
    required String path,
    required String name,
    required String mimeType,
    String targetDeviceId = '',
  }) async {
    final bridge = _nativeBridge;
    if (bridge == null) return false;
    try {
      return await bridge.enqueueFile(
        path: path,
        name: name,
        mimeType: mimeType,
        targetDeviceId: targetDeviceId,
      );
    } catch (_) {
      return false;
    }
  }

  void _removeDuplicateConnections(SessionConnection identifiedConnection) {
    final remoteId = identifiedConnection.peerInfo?.deviceId;
    if (remoteId == null || remoteId.isEmpty) return;
    final duplicates = _connections
        .where(
          (connection) =>
              !connection.isDisposed &&
              connection.state.isUsable &&
              connection.peerInfo?.deviceId == remoteId,
        )
        .toList();
    if (duplicates.length <= 1) return;

    final preferOutbound = _localIdentity.deviceId.compareTo(remoteId) < 0;
    final keep = duplicates.firstWhere(
      (connection) => connection.isOutbound == preferOutbound,
      orElse: () => identifiedConnection,
    );
    for (final duplicate in duplicates) {
      if (!identical(duplicate, keep)) duplicate.dispose(manual: false);
    }
  }

  void stopListener() {
    // The Android foreground service owns its native listener. The Flutter
    // Activity can be recreated without tearing down a healthy LAN session.
    if (_nativeBridge != null) {
      _isListening = false;
      return;
    }
    _serverSocket?.close();
    _serverSocket = null;
    _isListening = false;
    for (final connection in List<SessionConnection>.of(_connections)) {
      connection.dispose();
    }
    _connections.clear();
  }

  void dispose() {
    _nativeEventSubscription?.cancel();
    _nativeEventSubscription = null;
    _pendingNativeEvents.clear();
    stopListener();
    _connectionController.close();
    _connectionCreatedController.close();
    _networkPolicyController.close();
  }
}

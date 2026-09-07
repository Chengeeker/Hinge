import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'constants.dart';
import 'device_identity_manager.dart';
import 'protocol_frame.dart';
import 'trust_store.dart';

enum SessionState {
  disconnected,
  connecting,
  authenticating,
  connected,
  reconnecting,
}

class SessionPeerInfo {
  final String deviceId;
  final String name;
  final String manufacturer;
  final String model;
  final String platform;

  const SessionPeerInfo({
    required this.deviceId,
    required this.name,
    this.manufacturer = '',
    this.model = '',
    required this.platform,
  });
}

class SessionConnection {
  final Socket _socket;
  final DeviceIdentity _localIdentity;
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
  final bool startImmediately;
  final List<int> _outgoingBuffer = [];
  bool _writeInProgress = false;

  SessionState get state => _state;
  bool get isDisposed => _disposed;
  bool get isReady =>
      !_disposed && (_state == SessionState.connected || _peerInfo != null);
  Stream<ProtocolFrame> get frames => _frameController.stream;
  Stream<SessionState> get stateStream => _stateController.stream;
  Stream<SessionPeerInfo> get peerStream => _peerController.stream;
  SessionPeerInfo? get peerInfo => _peerInfo;
  DeviceIdentity get localIdentity => _localIdentity;
  String get remoteAddress => _socket.remoteAddress.address;

  /// Waits for the identity handshake to complete before a feature sends its
  /// first frame. This closes the small race where discovery has created a
  /// socket but the peer has not yet replied to SessionInit.
  Future<bool> waitUntilReady({
    Duration timeout = const Duration(seconds: 3),
  }) async {
    if (isReady) return true;
    if (_disposed) return false;
    try {
      await stateStream
          .firstWhere(
            (state) =>
                state == SessionState.connected ||
                state == SessionState.disconnected,
          )
          .timeout(timeout);
    } catch (_) {
      return isReady;
    }
    return isReady;
  }

  SessionConnection({
    required this._socket,
    required this._localIdentity,
    this.isOutbound = false,
    this.startImmediately = true,
  }) {
    try {
      _socket.setOption(SocketOption.tcpNoDelay, true);
    } catch (_) {}
    if (startImmediately) start();
  }

  /// Starts network I/O after the session manager has attached its lifecycle,
  /// peer-identity and command observers. A fast peer must not be able to send
  /// the first SessionInit before those observers exist.
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
  }

  void sendFrame(MessageType type, Uint8List payload) {
    if (_disposed) return;
    try {
      _outgoingBuffer.addAll(
        ProtocolFrame(type: type, payload: payload).serialize(),
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
      _socket.flush().whenComplete(() {
        _writeInProgress = false;
        if (_outgoingBuffer.isNotEmpty) _flushOutgoing();
      });
    } catch (_) {
      _writeInProgress = false;
      _updateState(SessionState.disconnected);
    }
  }

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
    });
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
    if (_missedHeartbeats > 3) {
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
        _sendSessionIdentity(MessageType.sessionAck);
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
      );
      if (_peerInfo?.deviceId == peer.deviceId) {
        // The peer may repeat SessionInit/SessionAck while both sides are
        // reconnecting. Do not leave a valid socket in the authenticating
        // state just because the identity payload did not change.
        if (_state == SessionState.authenticating ||
            _state == SessionState.connecting ||
            _state == SessionState.reconnecting) {
          _updateState(SessionState.connected);
        }
        return;
      }
      _peerInfo = peer;
      if (_state == SessionState.authenticating ||
          _state == SessionState.connecting ||
          _state == SessionState.reconnecting) {
        _updateState(SessionState.connected);
      }
      _peerController.add(peer);
    } catch (_) {
      // Ignore malformed identity frames without dropping the socket.
    }
  }

  void _updateState(SessionState newState) {
    if (_state == newState) return;
    _state = newState;
    if (!_stateController.isClosed) _stateController.add(newState);
    if (newState == SessionState.disconnected) dispose();
  }

  void dispose() {
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

class SessionManager {
  final DeviceIdentity _localIdentity;
  final TrustStore _trustStore;
  final int _listenPort;
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
  bool _isListening = false;
  String? _lastError;

  Stream<SessionConnection> get onClientConnected =>
      _connectionController.stream;
  Stream<SessionConnection> get onConnectionCreated =>
      _connectionCreatedController.stream;
  bool get isListening => _isListening;
  String? get lastError => _lastError;
  int get listeningPort => _serverSocket?.port ?? _listenPort;
  DeviceIdentity get localIdentity => _localIdentity;
  TrustStore get trustStore => _trustStore;

  SessionManager({
    required this._localIdentity,
    required this._trustStore,
    this._listenPort = AppConstants.sessionTcpPort,
  });

  SessionConnection? connectionForDevice(String deviceId) {
    for (final connection in _connections) {
      if (!connection.isDisposed &&
          connection.state == SessionState.connected &&
          connection.peerInfo?.deviceId == deviceId) {
        return connection;
      }
    }
    return null;
  }

  Future<void> startListener() async {
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
  ]) async {
    final socket = await Socket.connect(
      address,
      port,
      timeout: const Duration(seconds: 3),
    );
    return _registerConnection(socket, isOutbound: true);
  }

  void _onIncomingSocket(Socket socket) {
    final connection = _registerConnection(socket, isOutbound: false);
    _connectionController.add(connection);
  }

  SessionConnection _registerConnection(
    Socket socket, {
    required bool isOutbound,
  }) {
    final connection = SessionConnection(
      socket: socket,
      localIdentity: _localIdentity,
      isOutbound: isOutbound,
      startImmediately: false,
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

  void _removeDuplicateConnections(SessionConnection identifiedConnection) {
    final remoteId = identifiedConnection.peerInfo?.deviceId;
    if (remoteId == null || remoteId.isEmpty) return;
    final duplicates = _connections
        .where(
          (connection) =>
              !connection.isDisposed &&
              connection.state == SessionState.connected &&
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
      if (!identical(duplicate, keep)) duplicate.dispose();
    }
  }

  void stopListener() {
    _serverSocket?.close();
    _serverSocket = null;
    _isListening = false;
    for (final connection in List<SessionConnection>.of(_connections)) {
      connection.dispose();
    }
    _connections.clear();
  }

  void dispose() {
    stopListener();
    _connectionController.close();
    _connectionCreatedController.close();
  }
}

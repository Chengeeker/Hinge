import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';

import 'remote_input_cipher.dart';
import 'protocol_frame.dart';
import 'remote_input_model.dart';
import 'session_manager.dart';

class RemoteInputSender extends ChangeNotifier {
  final SessionConnection connection;
  final void Function(Uint8List)? desktopTransport;

  Uint8List? controlKey;
  final Future<Uint8List?> Function()? onEncryptionRequired;
  RemoteInputCipher? _cipher;
  Future<void> _outgoing = Future.value(), _incoming = Future.value();
  StreamSubscription<ProtocolFrame>? _frames;
  StreamSubscription<SessionState>? _states;
  Timer? _timeout;
  Timer? _keepAlive;
  bool _disposed = false;
  bool ready = false;
  bool supportsGameKeys = false;
  bool supportsAbsoluteMouse = false;
  bool _allowPlaintext = false;
  bool isEncrypted = false;
  String status = '正在验证远控密钥…';
  final StreamController<ProtocolFrame> _verified = StreamController.broadcast(
    sync: true,
  );
  Stream<ProtocolFrame> get verifiedFrames => _verified.stream;
  RemoteInputSender(
    this.connection, {
    this.controlKey,
    this.onEncryptionRequired,
    this.desktopTransport,
  });

  void _fail(String message) {
    _keepAlive?.cancel();
    ready = false;
    status = message;
    if (!_disposed) notifyListeners();
  }

  Future<void> _receive(ProtocolFrame frame) async {
    if (_disposed) return;
    if (frame.type == MessageType.remoteInput && frame.payload.length >= 16) {
      final reply = RemoteInputEvent.tryParse(frame.payload);
      if (reply != null && reply.flags & 2 != 0) {
        _timeout?.cancel();
        _allowPlaintext = true;
        isEncrypted = false;
        if (reply.actionType == RemoteActionType.controlAccepted) {
          _acceptControl(reply.flags);
        } else if (reply.actionType == RemoteActionType.controlEnded ||
            reply.actionType == RemoteActionType.controlDenied) {
          _fail('电脑端已停止或拒绝控制，请检查远控开关。');
        }
        _verified.add(frame);
        return;
      }
    }
    if (frame.type == MessageType.remoteInput &&
        frame.payload.length == 48 &&
        frame.payload[0] == 0x31 &&
        _cipher == null) {
      _timeout?.cancel();
      controlKey ??= await onEncryptionRequired?.call();
      if (_disposed || connection.state != SessionState.connected) return;
      if (controlKey == null) {
        _fail('电脑端要求加密。请在 Windows 开启加密并输入密钥，或关闭加密后重试。');
        return;
      }
      _timeout = Timer(
        const Duration(seconds: 8),
        () => _fail('验证超时，请核对密钥并重新连接。'),
      );
      _cipher = await RemoteInputCipher.create(
        controlKey!,
        frame.payload.sublist(16),
      );
      if (_disposed) return;
      _sendEvent(RemoteInputEvent(actionType: RemoteActionType.controlStart));
    } else if (frame.type == MessageType.secureRemoteInput && _cipher != null) {
      final bytes = await _cipher!.decrypt(frame.payload);
      if (bytes == null || _disposed) return;
      final event = RemoteInputEvent.tryParse(bytes);
      if (event == null) return;
      _timeout?.cancel();
      if (event.actionType == RemoteActionType.controlAccepted) {
        isEncrypted = true;
        _acceptControl(event.flags);
      } else if (event.actionType == RemoteActionType.controlEnded ||
          event.actionType == RemoteActionType.controlDenied) {
        _fail('电脑端已停止或拒绝控制，请检查远控开关。');
      }
      _verified.add(
        ProtocolFrame(type: MessageType.remoteInput, payload: bytes),
      );
    }
  }

  void _acceptControl(int flags) {
    ready = true;
    _keepAlive?.cancel();
    _keepAlive = Timer.periodic(const Duration(milliseconds: 500), (_) {
      if (ready && !_disposed) {
        _sendEvent(
          RemoteInputEvent(actionType: RemoteActionType.controlKeepAlive),
        );
      }
    });
    supportsGameKeys = flags & 1 != 0;
    status = isEncrypted ? '已连接 · 加密控制' : '已连接 · 局域网未加密';
    notifyListeners();
  }

  void sendKey(int key, bool down) {
    if (!ready || key < 8 || key > 254) return;
    _sendEvent(
      RemoteInputEvent(
        actionType: down ? RemoteActionType.keyDown : RemoteActionType.keyUp,
        buttonOrKey: key,
      ),
    );
  }

  void startControl() {
    _states = connection.stateStream.listen((state) {
      if (state != SessionState.connected) _fail('连接已断开，远控已停止。');
    });
    if (desktopTransport != null) {
      status = '正在连接桌面加密控制通道…';
      return;
    }
    _frames = connection.frames.listen((frame) {
      _incoming = _incoming
          .then((_) => _receive(frame))
          .catchError((Object _) => _fail('加密会话异常，请重新连接。'));
    }, onError: (Object _) => _fail('连接已断开。'));
    connection.sendFrame(
      MessageType.remoteInput,
      RemoteInputEvent(actionType: RemoteActionType.controlProbe).serialize(),
    );
    _timeout = Timer(
      const Duration(seconds: 8),
      () => _fail('验证超时，请核对密钥，并更新电脑端 Hinge。'),
    );
  }

  void stopControl() =>
      _sendEvent(RemoteInputEvent(actionType: RemoteActionType.controlStop));

  void setDesktopReady(bool allowed, {bool relativeMouse = false}) {
    if (_disposed || desktopTransport == null) return;
    ready = allowed;
    isEncrypted = true;
    supportsGameKeys = allowed && relativeMouse;
    status = allowed ? '已连接 · TLS 加密控制' : '电脑尚未允许控制，仍可查看桌面';
    _verified.add(
      ProtocolFrame(
        type: MessageType.remoteInput,
        payload: RemoteInputEvent(
          actionType: allowed
              ? RemoteActionType.controlAccepted
              : RemoteActionType.controlEnded,
          flags: 1,
        ).serialize(),
      ),
    );
    notifyListeners();
  }

  void sendMouseMove(int dx, int dy, {bool relative = false}) {
    if (relative && desktopTransport != null && !supportsGameKeys) return;
    final ev = RemoteInputEvent(
      actionType: RemoteActionType.mouseMove,
      flags: relative ? RemoteInputEvent.relativeMouseFlag : 0,
      deltaX: dx,
      deltaY: dy,
    );
    _sendEvent(ev);
  }

  void sendAbsoluteMouse(int x, int y) {
    if (!ready ||
        desktopTransport == null ||
        !supportsAbsoluteMouse ||
        x < 0 ||
        x > 65535 ||
        y < 0 ||
        y > 65535) {
      return;
    }
    _sendEvent(
      RemoteInputEvent(
        actionType: RemoteActionType.mouseMove,
        flags: RemoteInputEvent.absoluteMouseFlag,
        deltaX: x,
        deltaY: y,
      ),
    );
  }

  void sendMouseDown(RemoteMouseButton button) {
    final ev = RemoteInputEvent(
      actionType: RemoteActionType.mouseDown,
      buttonOrKey: button.value,
    );
    _sendEvent(ev);
  }

  void sendMouseUp(RemoteMouseButton button) {
    final ev = RemoteInputEvent(
      actionType: RemoteActionType.mouseUp,
      buttonOrKey: button.value,
    );
    _sendEvent(ev);
  }

  void sendMouseClick(RemoteMouseButton button) {
    final ev = RemoteInputEvent(
      actionType: RemoteActionType.mouseClick,
      buttonOrKey: button.value,
    );
    _sendEvent(ev);
  }

  void sendMouseDoubleClick() {
    final ev = RemoteInputEvent(
      actionType: RemoteActionType.mouseDoubleClick,
      buttonOrKey: RemoteMouseButton.left.value,
    );
    _sendEvent(ev);
  }

  void sendMouseScroll(int delta) {
    final ev = RemoteInputEvent(
      actionType: RemoteActionType.mouseScroll,
      wheelOrData: delta,
    );
    _sendEvent(ev);
  }

  void sendTextInput(String text) {
    if (text.isEmpty) return;
    if (desktopTransport != null && utf8.encode(text).length > 4000) {
      var chunk = StringBuffer();
      var size = 0;
      for (final rune in text.runes) {
        final character = String.fromCharCode(rune),
            bytes = utf8.encode(String.fromCharCode(rune)).length;
        if (size + bytes > 4000) {
          sendTextInput(chunk.toString());
          chunk = StringBuffer();
          size = 0;
        }
        chunk.write(character);
        size += bytes;
      }
      if (size > 0) sendTextInput(chunk.toString());
      return;
    }
    final ev = RemoteInputEvent(
      actionType: RemoteActionType.textInput,
      textPayload: text,
    );
    _sendEvent(ev);
  }

  @override
  void dispose() {
    _disposed = true;
    _timeout?.cancel();
    _keepAlive?.cancel();
    unawaited(_frames?.cancel());
    unawaited(_states?.cancel());
    unawaited(_verified.close());
    unawaited(
      _outgoing.whenComplete(
        () => controlKey?.fillRange(0, controlKey!.length, 0),
      ),
    );
    super.dispose();
  }

  void _sendEvent(RemoteInputEvent ev) {
    if (_disposed) return;
    if (desktopTransport != null) {
      if (ready) desktopTransport!(ev.serialize());
      return;
    }
    if (_allowPlaintext && ready) {
      connection.sendFrame(MessageType.remoteInput, ev.serialize());
      return;
    }
    if (_cipher == null) return;
    _outgoing = _outgoing
        .then((_) async {
          final bytes = await _cipher!.encrypt(ev.serialize());
          connection.sendFrame(MessageType.secureRemoteInput, bytes);
        })
        .catchError((Object _) => _fail('指令发送失败，请重新连接。'));
  }
}

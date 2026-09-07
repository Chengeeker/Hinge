import 'protocol_frame.dart';
import 'remote_input_model.dart';
import 'session_manager.dart';

class RemoteInputSender {
  final SessionConnection connection;

  RemoteInputSender(this.connection);

  void sendMouseMove(int dx, int dy) {
    final ev = RemoteInputEvent(
      actionType: RemoteActionType.mouseMove,
      deltaX: dx,
      deltaY: dy,
    );
    _sendEvent(ev);
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
    final ev = RemoteInputEvent(
      actionType: RemoteActionType.textInput,
      textPayload: text,
    );
    _sendEvent(ev);
  }

  void _sendEvent(RemoteInputEvent ev) {
    connection.sendFrame(MessageType.remoteInput, ev.serialize());
  }
}

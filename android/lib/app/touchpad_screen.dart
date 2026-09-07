import 'package:flutter/material.dart';

import '../core/remote_input_model.dart';
import '../core/remote_input_sender.dart';
import '../core/session_manager.dart';

class TouchpadScreen extends StatefulWidget {
  final String peerName;
  final SessionConnection connection;

  const TouchpadScreen({
    super.key,
    required this.peerName,
    required this.connection,
  });

  @override
  State<TouchpadScreen> createState() => _TouchpadScreenState();
}

class _TouchpadScreenState extends State<TouchpadScreen> {
  late final RemoteInputSender _sender;
  final TextEditingController _textInputController = TextEditingController();
  double _sensitivity = 1.5;

  @override
  void initState() {
    super.initState();
    _sender = RemoteInputSender(widget.connection);
  }

  @override
  void dispose() {
    _textInputController.dispose();
    super.dispose();
  }

  void _showTextInputDialog() {
    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('向电脑光标处发送文本'),
        content: TextField(
          controller: _textInputController,
          autofocus: true,
          decoration: const InputDecoration(
            hintText: '输入要发送到远程电脑光标处的文本...',
            border: OutlineInputBorder(),
          ),
          onSubmitted: (val) {
            if (val.isNotEmpty) {
              _sender.sendTextInput(val);
              _textInputController.clear();
              Navigator.pop(ctx);
            }
          },
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx),
            child: const Text('取消'),
          ),
          FilledButton.icon(
            icon: const Icon(Icons.send, size: 16),
            label: const Text('发送文本'),
            onPressed: () {
              final text = _textInputController.text;
              if (text.isNotEmpty) {
                _sender.sendTextInput(text);
                _textInputController.clear();
              }
              Navigator.pop(ctx);
            },
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(
        title: Text('无线遥控: ${widget.peerName}'),
        centerTitle: true,
        actions: [
          IconButton(
            icon: const Icon(Icons.keyboard),
            tooltip: '虚拟键盘输入',
            onPressed: _showTextInputDialog,
          ),
        ],
      ),
      body: Column(
        children: [
          // Sensitivity bar
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
            child: Row(
              children: [
                const Icon(Icons.speed, size: 18),
                const SizedBox(width: 8),
                Text(
                  '灵敏度: ${_sensitivity.toStringAsFixed(1)}x',
                  style: theme.textTheme.bodySmall,
                ),
                Expanded(
                  child: Slider(
                    value: _sensitivity,
                    min: 0.5,
                    max: 3.0,
                    divisions: 10,
                    onChanged: (val) => setState(() => _sensitivity = val),
                  ),
                ),
              ],
            ),
          ),

          // Main Trackpad Gesture Area
          Expanded(
            child: Container(
              margin: const EdgeInsets.fromLTRB(16, 0, 16, 8),
              decoration: BoxDecoration(
                color: theme.colorScheme.surfaceContainerHighest.withValues(
                  alpha: 0.5,
                ),
                borderRadius: BorderRadius.circular(20),
                border: Border.all(color: theme.colorScheme.outlineVariant),
              ),
              child: ClipRRect(
                borderRadius: BorderRadius.circular(20),
                child: GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onPanUpdate: (details) {
                    final dx = (details.delta.dx * _sensitivity).round();
                    final dy = (details.delta.dy * _sensitivity).round();
                    if (dx != 0 || dy != 0) {
                      _sender.sendMouseMove(dx, dy);
                    }
                  },
                  onTap: () {
                    _sender.sendMouseClick(RemoteMouseButton.left);
                  },
                  onDoubleTap: () {
                    _sender.sendMouseDoubleClick();
                  },
                  onSecondaryTap: () {
                    _sender.sendMouseClick(RemoteMouseButton.right);
                  },
                  child: Center(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Icon(
                          Icons.touch_app,
                          size: 48,
                          color: theme.colorScheme.primary.withValues(
                            alpha: 0.5,
                          ),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          '触控板手势区域\n单指：移动与鼠标左键 • 双指：鼠标右键\n双击：鼠标双击',
                          textAlign: TextAlign.center,
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: theme.colorScheme.outline,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),

          // Physical Buttons at Bottom
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
            child: Row(
              children: [
                Expanded(
                  flex: 3,
                  child: SizedBox(
                    height: 54,
                    child: FilledButton.tonal(
                      style: FilledButton.styleFrom(
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(12),
                        ),
                      ),
                      onPressed: () =>
                          _sender.sendMouseClick(RemoteMouseButton.left),
                      child: const Text('鼠标左键'),
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                SizedBox(
                  height: 54,
                  child: OutlinedButton(
                    onPressed: () => _sender.sendMouseScroll(120),
                    child: const Icon(Icons.arrow_upward),
                  ),
                ),
                const SizedBox(width: 4),
                SizedBox(
                  height: 54,
                  child: OutlinedButton(
                    onPressed: () => _sender.sendMouseScroll(-120),
                    child: const Icon(Icons.arrow_downward),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  flex: 3,
                  child: SizedBox(
                    height: 54,
                    child: FilledButton.tonal(
                      style: FilledButton.styleFrom(
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(12),
                        ),
                      ),
                      onPressed: () =>
                          _sender.sendMouseClick(RemoteMouseButton.right),
                      child: const Text('鼠标右键'),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 8),
        ],
      ),
    );
  }
}

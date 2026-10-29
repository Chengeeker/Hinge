import '../core/remote_input_cipher.dart';

import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../core/session_manager.dart';
import '../core/workspace_state.dart';
import 'touchpad_screen.dart';

class PhoneRemoteControlSettingsScreen extends StatefulWidget {
  final WorkspaceState workspaceState;
  final SessionManager sessionManager;
  final ValueChanged<SessionConnection?>? onActiveControlChanged;

  const PhoneRemoteControlSettingsScreen({
    super.key,
    required this.workspaceState,
    required this.sessionManager,
    this.onActiveControlChanged,
  });

  @override
  State<PhoneRemoteControlSettingsScreen> createState() =>
      _PhoneRemoteControlSettingsScreenState();
}

class _PhoneRemoteControlSettingsScreenState
    extends State<PhoneRemoteControlSettingsScreen> {
  static const _keyChannel = MethodChannel('hinge/game_controls');
  final Set<SessionConnection> _watchedConnections = <SessionConnection>{};
  final List<StreamSubscription<dynamic>> _subscriptions = [];
  SessionConnection? _activeControlConnection;

  bool get _enabled => widget.workspaceState.phoneRemoteControlEnabled;

  List<SessionConnection> get _connectedWindows => widget
      .sessionManager
      .activeConnections
      .where(
        (connection) =>
            connection.state == SessionState.connected &&
            (connection.peerInfo?.platform.toLowerCase().contains('windows') ??
                false),
      )
      .toList();

  @override
  void initState() {
    super.initState();
    widget.workspaceState.addListener(_refresh);
    for (final connection in widget.sessionManager.activeConnections) {
      _watchConnection(connection);
    }
    _subscriptions.add(
      widget.sessionManager.onConnectionCreated.listen(_watchConnection),
    );
  }

  @override
  void dispose() {
    widget.workspaceState.removeListener(_refresh);
    for (final subscription in _subscriptions) {
      unawaited(subscription.cancel());
    }
    super.dispose();
  }

  void _watchConnection(SessionConnection connection) {
    if (!_watchedConnections.add(connection)) return;
    _subscriptions.add(connection.stateStream.listen((_) => _refresh()));
    _subscriptions.add(connection.peerStream.listen((_) => _refresh()));
  }

  void _refresh() {
    if (mounted) setState(() {});
  }

  String get _status {
    if (!_enabled) return '已关闭';
    final activeName = _activeControlConnection?.peerInfo?.name;
    if (activeName != null) return '正在控制 $activeName';
    final count = _connectedWindows.length;
    return count == 0 ? '已开启 · 等待 Windows 电脑连接' : '已开启 · $count 台电脑在线';
  }

  Future<void> _openTouchpad(
    SessionConnection connection, {
    bool desktop = false,
  }) async {
    if (!_enabled || connection.state != SessionState.connected) return;
    final peerName = connection.peerInfo?.name ?? 'Windows 电脑';
    final peerId = connection.peerInfo?.deviceId;
    if (peerId == null || peerId.trim().isEmpty) return;
    Uint8List? key;
    try {
      key = await _keyChannel.invokeMethod<Uint8List>('loadRemoteControlKey', {
        'peerId': peerId,
      });
    } on PlatformException {
      try {
        await _keyChannel.invokeMethod<bool>('deleteRemoteControlKey', {
          'peerId': peerId,
        });
      } on PlatformException {
        // A missing Android Keystore entry falls back to asking only if encryption is enabled.
      }
    }
    if (!mounted || connection.state != SessionState.connected) {
      key?.fillRange(0, key.length, 0);
      return;
    }
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => TouchpadScreen(
          showDesktop: desktop,
          peerName: peerName,
          controlKey: key,
          onEncryptionRequired: () => _requestRemoteControlKey(peerId),
          connection: connection,
          onControlStateChanged: (active) {
            if (!mounted) return;
            setState(() {
              _activeControlConnection = active ? connection : null;
            });
            widget.onActiveControlChanged?.call(active ? connection : null);
          },
        ),
      ),
    );
    if (!mounted) return;
    setState(() => _activeControlConnection = null);
    widget.onActiveControlChanged?.call(null);
  }

  Future<Uint8List?> _requestRemoteControlKey(String peerId) async {
    if (!mounted) return null;
    final tokenController = TextEditingController();
    var remember = true;
    final choice = await showDialog<Map<String, Object?>>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: const Text('加密远控密钥'),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                TextField(
                  controller: tokenController,
                  obscureText: true,
                  autocorrect: false,
                  enableSuggestions: false,
                  decoration: const InputDecoration(
                    labelText: 'Windows 投屏与远控 → 手机远控电脑 → 密钥',
                    hintText: 'HRC1-…',
                  ),
                ),
                CheckboxListTile(
                  contentPadding: EdgeInsets.zero,
                  value: remember,
                  onChanged: (value) =>
                      setDialogState(() => remember = value ?? true),
                  title: const Text('安全保存在此手机'),
                  subtitle: const Text('使用 Android Keystore 加密保护'),
                ),
                const Text('仅在电脑启用远控加密时需要输入。'),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext),
              child: const Text('取消'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(dialogContext, {
                'token': tokenController.text,
                'remember': remember,
              }),
              child: const Text('连接'),
            ),
          ],
        ),
      ),
    );
    tokenController.dispose();
    if (!mounted || choice == null) return null;
    late final Uint8List key;
    try {
      key = RemoteInputCipher.parseToken(choice['token'] as String? ?? '');
    } on FormatException {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('密钥格式不正确，请从电脑端完整复制 HRC1- 密钥。')),
      );
      return null;
    }
    if (choice['remember'] == true) {
      try {
        await _keyChannel.invokeMethod<bool>('saveRemoteControlKey', {
          'peerId': peerId,
          'key': key,
        });
      } on PlatformException {
        key.fillRange(0, key.length, 0);
        if (mounted) {
          ScaffoldMessenger.of(
            context,
          ).showSnackBar(const SnackBar(content: Text('无法安全保存密钥，请重试并关闭安全保存。')));
        }
        return null;
      }
    }
    if (!mounted) {
      key.fillRange(0, key.length, 0);
      return null;
    }
    return key;
  }

  Future<void> _forgetRemoteKey(SessionConnection connection) async {
    final peerId = connection.peerInfo?.deviceId;
    if (peerId == null) return;
    try {
      await _keyChannel.invokeMethod<bool>('deleteRemoteControlKey', {
        'peerId': peerId,
      });
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('已清除此电脑的手机密钥，下次连接时可重新输入。')),
        );
      }
    } on PlatformException {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('无法清除手机保存的密钥。')));
      }
    }
  }

  Future<void> _forgetDesktopIdentity(SessionConnection connection) async {
    final peer = connection.peerInfo?.deviceId;
    if (peer == null) return;
    try {
      await const MethodChannel('hinge/desktop_video')
          .invokeMethod<void>('forgetPeer', {'peerId': peer});
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('已清除桌面身份，下次连接需重新核对。')));
      }
    } on PlatformException {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('无法清除桌面身份。')));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final computers = _connectedWindows;
    return Scaffold(
      appBar: AppBar(title: const Text('手机控制电脑')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
          children: [
            Card(
              child: SwitchListTile.adaptive(
                value: _enabled,
                onChanged: widget.workspaceState.setPhoneRemoteControlEnabled,
                title: const Text('启用手机远控电脑'),
                subtitle: const Text('开启后，可用此手机控制已连接的 Windows 电脑。'),
                secondary: const Icon(Icons.touch_app_outlined),
              ),
            ),
            const SizedBox(height: 12),
            Card(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('当前状态', style: theme.textTheme.titleMedium),
                    const SizedBox(height: 8),
                    Text(_status, style: theme.textTheme.bodyLarge),
                    const SizedBox(height: 8),
                    Text(
                      '单指滑动移动鼠标，轻点左键，双指轻点右键，双指滑动滚动；双击后按住并拖动可拖拽。',
                      style: theme.textTheme.bodyMedium,
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(height: 20),
            Text('已连接的 Windows 电脑', style: theme.textTheme.titleMedium),
            const SizedBox(height: 8),
            if (computers.isEmpty)
              const Card(
                child: ListTile(
                  leading: Icon(Icons.computer_outlined),
                  title: Text('没有已连接的 Windows 电脑'),
                  subtitle: Text('请先在 Hinge 首页连接电脑，再返回此处打开触控板。'),
                ),
              )
            else
              for (final connection in computers)
                Card(
                  child: Column(
                    children: [
                      ListTile(
                        leading: const Icon(Icons.computer_outlined),
                        title: Text(connection.peerInfo?.name ?? 'Windows 电脑'),
                        subtitle: Text(
                          identical(connection, _activeControlConnection)
                              ? '正在控制'
                              : '已连接 · 等待开始控制',
                        ),
                        trailing: PopupMenuButton<String>(
                          tooltip: '远控身份选项',
                          onSelected: (value) => value == 'desktop'
                              ? _forgetDesktopIdentity(connection)
                              : _forgetRemoteKey(connection),
                          itemBuilder: (_) => const [
                            PopupMenuItem(
                              value: 'desktop',
                              child: Text('清除此电脑的桌面身份'),
                            ),
                            PopupMenuItem(
                              value: 'forget',
                              child: Text('清除此电脑的已保存密钥'),
                            ),
                          ],
                        ),
                      ),
                      Padding(
                        padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
                        child: Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: [
                            FilledButton.icon(
                              label: const Text('电脑桌面'),
                              onPressed:
                                  _enabled && _activeControlConnection == null
                                  ? () =>
                                        _openTouchpad(connection, desktop: true)
                                  : null,
                              icon: const Icon(Icons.desktop_windows, size: 18),
                            ),
                            FilledButton.tonal(
                              onPressed:
                                  _enabled && _activeControlConnection == null
                                  ? () => _openTouchpad(connection)
                                  : null,
                              child: const Text('打开触控板'),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
            const SizedBox(height: 12),
            Text(
              '电脑端需开启桌面共享与远控。首次连接核对身份码；独立触控板仅在电脑开启可选加密时需要密钥。',
              style: theme.textTheme.bodySmall?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

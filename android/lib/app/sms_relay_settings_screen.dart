import 'package:flutter/material.dart';
import 'package:material_symbols_icons/symbols.dart';

import '../core/workspace_data_service.dart';

/// Explicit opt-in for the sensitive SMS/MMS receive broadcast. Hinge does
/// not read the existing inbox; it only forwards new broadcasts while a
/// trusted desktop session is connected.
class SmsRelaySettingsScreen extends StatefulWidget {
  final WorkspaceDataService dataService;

  const SmsRelaySettingsScreen({super.key, required this.dataService});

  @override
  State<SmsRelaySettingsScreen> createState() => _SmsRelaySettingsScreenState();
}

class _SmsRelaySettingsScreenState extends State<SmsRelaySettingsScreen>
    with WidgetsBindingObserver {
  bool _enabled = false;
  bool _smsPermission = false;
  bool _readSmsPermission = false;
  bool _mmsPermission = false;
  bool _notificationAccess = false;
  bool _loading = true;
  bool _working = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _load();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _load();
  }

  Future<void> _load() async {
    final results = await Future.wait<dynamic>([
      widget.dataService.smsRelayEnabled(),
      widget.dataService.smsPermissionStatus(),
      widget.dataService.smsNotificationAccessEnabled(),
    ]);
    if (!mounted) return;
    final permissions = results[1] as Map<String, bool>;
    setState(() {
      _enabled = results[0] as bool;
      _smsPermission = permissions['sms'] == true;
      _readSmsPermission = permissions['readSms'] == true;
      _mmsPermission = permissions['mms'] == true;
      _notificationAccess = results[2] as bool;
      _loading = false;
    });
  }

  Future<void> _setEnabled(bool enabled) async {
    if (_working) return;
    setState(() => _working = true);
    try {
      var smsPermission = _smsPermission;
      var readSmsPermission = _readSmsPermission;
      var mmsPermission = _mmsPermission;
      if (enabled && (!smsPermission || !readSmsPermission)) {
        final permissions = await widget.dataService.requestSmsPermissions();
        smsPermission = permissions['sms'] == true;
        readSmsPermission = permissions['readSms'] == true;
        mmsPermission = permissions['mms'] == true;
      }
      if (enabled && !smsPermission) {
        if (mounted) {
          _showMessage('未获得“接收短信”权限，短信同步未开启');
        }
        return;
      }
      final applied = await widget.dataService.setSmsRelayEnabled(enabled);
      if (!mounted) return;
      setState(() {
        _enabled = applied;
        _smsPermission = smsPermission;
        _readSmsPermission = readSmsPermission;
        _mmsPermission = mmsPermission;
      });
    } finally {
      if (mounted) setState(() => _working = false);
    }
  }

  void _showMessage(String message) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  String _permissionText(bool granted) => granted ? '已允许' : '未允许';

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: const Text('短信同步')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '将新短信转发到 Windows',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  const SizedBox(height: 8),
                  const Text(
                    '仅在你主动开启、并且已建立可信 Windows 会话时转发。Hinge 不读取短信历史，也不会把短信保存到本地文件。验证码通知会提供“复制验证码”按钮。',
                  ),
                  const SizedBox(height: 8),
                  SwitchListTile.adaptive(
                    contentPadding: EdgeInsets.zero,
                    secondary: Icon(Symbols.sms_rounded, color: scheme.primary),
                    title: const Text('短信同步'),
                    subtitle: Text(
                      _loading
                          ? '检查中…'
                          : _enabled
                          ? '已开启，仅发送给当前可信会话'
                          : '已关闭',
                    ),
                    value: _enabled,
                    onChanged: _working ? null : _setEnabled,
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          Card(
            child: Column(
              children: [
                _permissionTile(
                  icon: Symbols.sms_rounded,
                  title: '接收短信',
                  subtitle: '监听新到的 SMS 和验证码短信',
                  status: _permissionText(_smsPermission),
                ),
                const Divider(height: 1),
                _permissionTile(
                  icon: Symbols.mark_email_read_rounded,
                  title: '访问短信/彩信',
                  subtitle: '读取新短信内容，并兼容部分验证码广播限制',
                  status: _permissionText(_readSmsPermission),
                ),
                const Divider(height: 1),
                _permissionTile(
                  icon: Symbols.perm_media_rounded,
                  title: '接收彩信',
                  subtitle: _mmsPermission ? '可以提示新到彩信' : '部分系统会限制第三方应用接收彩信广播',
                  status: _permissionText(_mmsPermission),
                ),
                const Divider(height: 1),
                ListTile(
                  leading: Icon(
                    Symbols.settings_rounded,
                    color: scheme.primary,
                  ),
                  title: const Text('打开应用权限设置'),
                  subtitle: const Text('可在系统页面重新检查短信和彩信权限'),
                  trailing: const Icon(Symbols.chevron_right_rounded),
                  onTap: widget.dataService.openAppSettings,
                ),
                const Divider(height: 1),
                ListTile(
                  leading: SizedBox(
                    width: 40,
                    child: Center(
                      child: Icon(
                        Symbols.notifications_active_rounded,
                        color: scheme.primary,
                      ),
                    ),
                  ),
                  title: const Text('短信通知读取（兼容模式）'),
                  subtitle: const Text('用于厂商系统拦截短信广播时，从短信应用通知中取得新消息'),
                  trailing: Text(_notificationAccess ? '已开启' : '去开启'),
                  onTap: _notificationAccess
                      ? null
                      : widget.dataService.openSmsNotificationAccessSettings,
                ),
              ],
            ),
          ),
          const SizedBox(height: 16),
          FilledButton.tonalIcon(
            onPressed: !_enabled
                ? null
                : () async {
                    final sent = await widget.dataService
                        .sendSmsRelayTestEvent();
                    if (!mounted) return;
                    _showMessage(
                      sent ? '测试消息已进入同步链路，请检查 Windows 的应用内提示' : '请先开启短信同步',
                    );
                  },
            icon: const Icon(Symbols.send_rounded),
            label: const Text('发送测试消息到 Windows'),
          ),
          const SizedBox(height: 16),
          Card(
            color: scheme.surfaceContainerLow,
            child: const Padding(
              padding: EdgeInsets.all(16),
              child: Text(
                '说明：Android 将短信和彩信权限列为高敏感权限，部分系统还会限制验证码广播。兼容模式只处理系统短信应用发出的消息通知，忽略其他应用通知；正文和验证码仅在内存中处理，并通过已连接的可信会话发送。',
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _permissionTile({
    required IconData icon,
    required String title,
    required String subtitle,
    required String status,
  }) {
    final theme = Theme.of(context);
    return ListTile(
      leading: SizedBox(
        width: 40,
        child: Center(child: Icon(icon, color: theme.colorScheme.primary)),
      ),
      title: Text(title),
      subtitle: Text(subtitle),
      trailing: Text(status),
    );
  }
}

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:material_symbols_icons/symbols.dart';

import '../core/workspace_data_service.dart';

/// Android 的后台限制由系统和厂商共同决定，因此这里展示的是一份
/// 可验证的设置清单，而不是承诺“绝对保活”。每个入口都跳转到系统页面，
/// 返回应用后会重新读取状态。
class KeepAliveSettingsScreen extends StatefulWidget {
  final WorkspaceDataService dataService;

  const KeepAliveSettingsScreen({super.key, required this.dataService});

  @override
  State<KeepAliveSettingsScreen> createState() =>
      _KeepAliveSettingsScreenState();
}

class _KeepAliveSettingsScreenState extends State<KeepAliveSettingsScreen>
    with WidgetsBindingObserver {
  Map<String, dynamic> _status = const {};
  bool _loading = true;

  bool get _notificationEnabled => _status['notificationPermission'] == true;
  bool get _persistentNotification =>
      _status['persistentNotification'] != false;
  bool get _batteryOptimizationIgnored =>
      _status['batteryOptimizationIgnored'] == true;
  bool get _companionSupported => _status['companionSupported'] == true;
  bool get _companionAssociated => _status['companionAssociated'] == true;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _loadStatus();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _loadStatus();
  }

  Future<void> _loadStatus() async {
    final status = await widget.dataService.keepAliveStatus();
    if (!mounted) return;
    setState(() {
      _status = status;
      _loading = false;
    });
  }

  Future<void> _setPersistentNotification(bool enabled) async {
    if (enabled && !_notificationEnabled) {
      final granted = await widget.dataService.requestNotificationPermission();
      if (!granted) {
        if (mounted) _showMessage('请先允许通知，常驻通知才能显示');
        await _loadStatus();
        return;
      }
    }
    await widget.dataService.setPersistentNotificationEnabled(enabled);
    await _loadStatus();
  }

  Future<void> _openSetting(
    Future<bool> Function() opener,
    String unavailableMessage,
  ) async {
    final opened = await opener();
    if (!opened && mounted) _showMessage(unavailableMessage);
  }

  Future<void> _associateCompanion() async {
    final associated = await widget.dataService.associateCompanion();
    if (!mounted) return;
    if (!associated) {
      _showMessage('没有完成 Windows BLE 自动唤醒绑定；请先在 Windows 托盘开启配对广播');
    } else {
      _showMessage('Windows 自动唤醒已绑定');
    }
    await _loadStatus();
  }

  Future<void> _removeCompanion() async {
    final removed = await widget.dataService.removeCompanion();
    if (!mounted) return;
    _showMessage(removed ? '已解除 Windows 自动唤醒绑定' : '解除绑定失败，请重试');
    await _loadStatus();
  }

  Future<void> _copyConnectionDiagnostics() async {
    final diagnostics = await widget.dataService.readConnectionDiagnostics();
    if (!mounted) return;
    if (diagnostics.trim().isEmpty) {
      _showMessage('暂时没有连接诊断记录');
      return;
    }
    await Clipboard.setData(ClipboardData(text: diagnostics));
    if (mounted) _showMessage('连接诊断已复制，可直接发给开发者排查');
  }

  void _showMessage(String message) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  String _statusText(bool enabled) => _loading
      ? '检查中…'
      : enabled
      ? '已开启'
      : '需要设置';

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: const Text('保活设置')),
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
                    '让 Hinge 在后台保持待命',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  const SizedBox(height: 6),
                  Text(
                    'Hinge 使用 Android 前台服务提供局域网发现、连接恢复和唤醒入口。无操作时会进入低功耗待命；不同厂商还可能需要额外允许后台运行。',
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                  const SizedBox(height: 8),
                  SwitchListTile.adaptive(
                    contentPadding: EdgeInsets.zero,
                    secondary: Icon(
                      Symbols.notifications_active_rounded,
                      color: scheme.primary,
                    ),
                    title: const Text('常驻通知'),
                    subtitle: const Text('在通知中心保留 Hinge 的后台状态，并提供点击唤醒入口'),
                    value: _persistentNotification,
                    onChanged: _setPersistentNotification,
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    'Windows 自动唤醒（实验性）',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  const SizedBox(height: 6),
                  const Text(
                    '绑定后，Windows 右键发送文件时会先发一个很短的 BLE 唤醒信号。Android 被系统唤醒后恢复原有局域网连接；BLE 信号不携带文件、验证码或配对码。',
                  ),
                  const SizedBox(height: 10),
                  Text(
                    !_companionSupported
                        ? '当前系统没有 Companion Device 支持'
                        : _companionAssociated
                        ? '已绑定 Windows BLE 唤醒设备'
                        : '尚未绑定',
                    style: Theme.of(context).textTheme.labelLarge,
                  ),
                  const SizedBox(height: 8),
                  Row(
                    children: [
                      if (_companionSupported && !_companionAssociated)
                        FilledButton.tonal(
                          onPressed: _loading ? null : _associateCompanion,
                          child: const Text('开始绑定'),
                        ),
                      if (_companionSupported && _companionAssociated)
                        OutlinedButton(
                          onPressed: _loading ? null : _removeCompanion,
                          child: const Text('解除绑定'),
                        ),
                    ],
                  ),
                  const SizedBox(height: 6),
                  Text(
                    '绑定时请先在 Windows Hinge 托盘菜单或设置页开启“60 秒 BLE 配对广播”。',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 16),
          Text('保活清单', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          Card(
            child: Column(
              children: [
                _settingTile(
                  icon: Symbols.notifications_rounded,
                  title: '通知设置',
                  subtitle: _notificationEnabled
                      ? '已允许接收文件和连接状态通知'
                      : '未允许，常驻通知和文件接收提醒无法显示',
                  status: _statusText(_notificationEnabled),
                  onTap: () => _openSetting(
                    widget.dataService.openNotificationSettings,
                    '当前设备没有可用的通知设置页面',
                  ),
                  action: _notificationEnabled ? null : '去开启',
                ),
                const Divider(height: 1),
                _settingTile(
                  icon: Symbols.battery_saver_rounded,
                  title: '后台高耗电设置',
                  subtitle: _batteryOptimizationIgnored
                      ? '已加入系统电池优化白名单'
                      : '降低后台休眠时被系统限制的概率；不保证永久保持 TCP',
                  status: _statusText(_batteryOptimizationIgnored),
                  onTap: () => _openSetting(
                    widget.dataService.openBatteryOptimizationSettings,
                    '当前设备没有可用的电池优化设置页面',
                  ),
                  action: _batteryOptimizationIgnored ? null : '去设置',
                ),
                const Divider(height: 1),
                _settingTile(
                  icon: Symbols.lock_clock_rounded,
                  title: '锁定后台 / 自启动',
                  subtitle: '打开厂商的后台保护页面，将 Hinge 加入允许后台运行的清单',
                  status: '按设备提供',
                  onTap: () => _openSetting(
                    widget.dataService.openBackgroundProtectionSettings,
                    '当前设备没有厂商专用入口，已无法直达',
                  ),
                  action: '去设置',
                ),
                const Divider(height: 1),
                _settingTile(
                  icon: Symbols.settings_applications_rounded,
                  title: '应用详情',
                  subtitle: '手动检查通知、电池、后台数据和权限状态',
                  status: '系统页面',
                  onTap: widget.dataService.openAppSettings,
                  action: '打开',
                ),
                const Divider(height: 1),
                _settingTile(
                  icon: Symbols.content_copy_rounded,
                  title: '复制连接诊断',
                  subtitle: '导出服务、网络、心跳和重连记录，不包含文件内容、验证码或配对码',
                  status: '本机日志',
                  onTap: _copyConnectionDiagnostics,
                  action: '复制',
                ),
              ],
            ),
          ),
          const SizedBox(height: 16),
          Card(
            color: scheme.surfaceContainerLow,
            child: const Padding(
              padding: EdgeInsets.all(16),
              child: Text(
                '说明：自动唤醒的目标是“需要发送时再恢复连接”，不是永久维持 TCP。Android 不允许普通应用绕过系统和厂商的后台管理；BLE、局域网或 Companion 不可用时，常驻通知仍是可靠的人工兜底入口。',
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _settingTile({
    required IconData icon,
    required String title,
    required String subtitle,
    required String status,
    required VoidCallback onTap,
    String? action,
  }) {
    final theme = Theme.of(context);
    final colorScheme = theme.colorScheme;

    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsetsDirectional.fromSTEB(16, 14, 16, 14),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.center,
          children: [
            SizedBox(
              width: 48,
              child: Center(
                child: Icon(icon, color: colorScheme.primary, size: 28),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: theme.textTheme.titleMedium),
                  const SizedBox(height: 4),
                  Text(subtitle, style: theme.textTheme.bodyMedium),
                ],
              ),
            ),
            const SizedBox(width: 12),
            SizedBox(
              width: 104,
              child: Center(
                child: action == null
                    ? Text(
                        status,
                        textAlign: TextAlign.center,
                        style: theme.textTheme.labelMedium,
                      )
                    : FilledButton.tonal(
                        onPressed: onTap,
                        style: FilledButton.styleFrom(
                          minimumSize: const Size(96, 44),
                          padding: const EdgeInsets.symmetric(horizontal: 12),
                        ),
                        child: Text(action),
                      ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

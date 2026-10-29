import 'package:flutter/material.dart';

import '../core/workspace_data_service.dart';
import 'page_components.dart';

class ScreenControlScreen extends StatefulWidget {
  final WorkspaceDataService dataService;
  final Future<void> Function()? onOpenComputer;
  final String Function()? computerStatus;

  const ScreenControlScreen({
    super.key,
    required this.dataService,
    this.onOpenComputer,
    this.computerStatus,
  });

  @override
  State<ScreenControlScreen> createState() => _ScreenControlScreenState();
}

class _ScreenControlScreenState extends State<ScreenControlScreen>
    with WidgetsBindingObserver {
  Map<String, dynamic> _status = const {};
  bool _opening = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _refresh();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _refresh();
  }

  Future<void> _refresh() async {
    Map<String, dynamic> status;
    try {
      status = await widget.dataService.screenControlStatus();
    } catch (_) {
      status = const {};
    }
    if (mounted) setState(() => _status = status);
  }

  Future<void> _openSettings() async {
    if (_opening) return;
    setState(() => _opening = true);
    var opened = false;
    try {
      opened = await widget.dataService.openWirelessDebuggingSettings();
    } catch (_) {
      opened = false;
    }
    if (!mounted) return;
    setState(() => _opening = false);
    if (!opened) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('无法打开系统设置，请手动进入开发者选项中的无线调试。')),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final supported = _status['supported'] as bool?;
    final developerEnabled = _status['developerOptionsEnabled'] as bool?;
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('投屏与远控')),
      body: SafeArea(
        child: HingePageBody(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (widget.onOpenComputer != null) ...[
                Card(
                  child: ListTile(
                    leading: const Icon(Icons.computer_outlined),
                    title: const Text('手机控制电脑'),
                    subtitle: Text(
                      widget.computerStatus?.call() ?? '电脑桌面、触控板与游戏模式',
                    ),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () async {
                      await widget.onOpenComputer!();
                      if (mounted) setState(() {});
                    },
                  ),
                ),
                const SizedBox(height: 16),
              ],
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text('电脑控制手机', style: theme.textTheme.titleMedium),
                      const SizedBox(height: 8),
                      Text(
                        supported == false
                            ? '需要 Android 11 及以上系统。'
                            : developerEnabled == false
                            ? '请先开启开发者选项和无线调试。'
                            : developerEnabled == true
                            ? '开发者选项已开启，请在系统设置确认无线调试状态。'
                            : '请在系统设置中开启无线调试。',
                      ),
                      const SizedBox(height: 16),
                      FilledButton.icon(
                        onPressed: supported == false || _opening
                            ? null
                            : _openSettings,
                        icon: const Icon(Icons.settings_outlined),
                        label: Text(_opening ? '正在打开…' : '打开系统调试设置'),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 24),
              Text('连接说明', style: theme.textTheme.titleMedium),
              const SizedBox(height: 8),
              const Text('手机与电脑连接同一网络。开启无线调试后，在电脑的“投屏与远控”中完成配对并开始投屏。仅向可信电脑授权。'),
            ],
          ),
        ),
      ),
    );
  }
}

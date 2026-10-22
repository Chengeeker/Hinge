import 'package:flutter/material.dart';

import '../core/workspace_data_service.dart';
import 'page_components.dart';

class ScreenControlScreen extends StatefulWidget {
  final WorkspaceDataService dataService;

  const ScreenControlScreen({super.key, required this.dataService});

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
              const HingeSectionTitle(
                title: '在电脑上使用手机',
                subtitle: '电脑端已集成投屏组件，无需另外下载软件。投屏和远控由 Hinge Windows 客户端发起。',
              ),
              const SizedBox(height: 24),
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text('准备无线调试', style: theme.textTheme.titleMedium),
                      const SizedBox(height: 8),
                      Text(
                        supported == false
                            ? '当前系统低于 Android 11，无法使用本功能所需的安全无线调试。'
                            : developerEnabled == false
                            ? '开发者选项尚未开启，请按系统引导先启用开发者选项，再打开无线调试。'
                            : developerEnabled == true
                            ? '开发者选项已开启。请到系统设置确认无线调试已开启；此处不代表电脑已连接或正在投屏。'
                            : '需要 Android 11 及以上系统。请在系统开发者选项中确认无线调试状态。',
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
              Text('首次连接', style: theme.textTheme.titleMedium),
              const SizedBox(height: 8),
              const Text(
                '1. 手机和电脑连接同一局域网，手机开启无线调试。\n\n'
                '2. 电脑打开 Hinge → 设置 → 手机投屏与远控，点击“无线调试配对设置”。在手机无线调试中选择“使用配对码配对设备”，将系统显示的配对码填入电脑端完成配对。\n\n'
                '3. 返回电脑的“手机投屏与远控”，点击“仅投屏”或“投屏并控制”。后续复用已有配对；端口变化时由电脑尝试自动发现。',
              ),
              const SizedBox(height: 24),
              Text('画面与性能', style: theme.textTheme.titleMedium),
              const SizedBox(height: 8),
              const Text(
                '电脑端可自定义帧率上限、视频码率和画面最长边，并选择 H.264、H.265 或 AV1。'
                '帧率上限不等于锁定输出；静止画面或低帧率视频不会持续输出 60 帧。电脑页面会显示实际渲染帧率。\n\n'
                'H.265 通常能改善同等带宽下的画质，但不一定降低延迟；AV1 需要手机支持编码。'
                '启动失败可切换 H.264，帧率不足可尝试降低分辨率或码率。',
              ),
              const SizedBox(height: 24),
              Text('结束与授权', style: theme.textTheme.titleMedium),
              const SizedBox(height: 8),
              const Text(
                '关闭电脑上的投屏窗口，或点击电脑端“停止”，即可结束本次投屏。'
                '手机关闭无线调试也会中断调试连接，并影响依赖该连接的剪贴板同步。'
                '不再使用这台电脑时，可在系统无线调试的已配对设备中撤销其授权。\n\n'
                '仅向自己的可信电脑提供配对码。已授权电脑具有调试和控制能力；'
                '部分手机还需要在系统中单独允许调试输入。当前页面不显示正在投屏状态，'
                '也不支持手机观看或控制电脑画面。',
              ),
            ],
          ),
        ),
      ),
    );
  }
}

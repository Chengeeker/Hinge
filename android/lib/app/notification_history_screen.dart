import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:material_symbols_icons/symbols.dart';

import '../core/notification_history_model.dart';
import '../core/workspace_data_service.dart';

/// Displays the notification records collected by Android's
/// NotificationListenerService. Records stay in the app-private SQLite
/// database and are loaded in pages so a busy notification center cannot
/// freeze the workspace UI.
class NotificationHistoryScreen extends StatefulWidget {
  final WorkspaceDataService dataService;

  const NotificationHistoryScreen({super.key, required this.dataService});

  @override
  State<NotificationHistoryScreen> createState() =>
      _NotificationHistoryScreenState();
}

class _NotificationHistoryScreenState extends State<NotificationHistoryScreen>
    with WidgetsBindingObserver {
  static const int _pageSize = 100;
  static const EventChannel _events = EventChannel(
    'hinge/notification_history/events',
  );

  final List<NotificationHistoryItem> _items = [];
  List<NotificationHistoryApplication> _applications = [];
  StreamSubscription<dynamic>? _eventSubscription;
  Timer? _refreshDebounce;
  // Empty string is the explicit "all applications" sentinel. Keeping the
  // dropdown value non-null makes Flutter render that item instead of leaving
  // the field visually empty.
  String _selectedPackage = '';
  bool _ascending = true;
  bool _accessEnabled = false;
  bool _enabled = false;
  bool _loading = true;
  bool _loadingMore = false;
  String? _error;
  int _total = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (Platform.isAndroid) {
      _eventSubscription = _events.receiveBroadcastStream().listen(
        (_) => _scheduleRefresh(),
        onError: (_, _) {},
      );
    }
    _load(reset: true);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _eventSubscription?.cancel();
    _refreshDebounce?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _load(reset: true);
  }

  void _scheduleRefresh() {
    _refreshDebounce?.cancel();
    _refreshDebounce = Timer(const Duration(milliseconds: 300), () {
      if (mounted) _load(reset: true);
    });
  }

  Future<void> _load({required bool reset}) async {
    if (_loadingMore || (!reset && _items.length >= _total)) return;
    if (reset) {
      setState(() {
        _loading = true;
        _error = null;
      });
    } else {
      setState(() => _loadingMore = true);
    }

    try {
      final status = await Future.wait<dynamic>([
        widget.dataService.notificationHistoryAccessEnabled(),
        widget.dataService.notificationHistoryEnabled(),
      ]);
      final access = status[0] == true;
      final enabled = status[1] == true;
      if (!access || !enabled) {
        if (!mounted) return;
        setState(() {
          _accessEnabled = access;
          _enabled = enabled;
          _items.clear();
          _applications = [];
          _total = 0;
          _loading = false;
          _loadingMore = false;
        });
        return;
      }

      final page = await widget.dataService.loadNotificationHistory(
        offset: reset ? 0 : _items.length,
        limit: _pageSize,
        ascending: _ascending,
        packageName: _selectedPackage.isEmpty ? null : _selectedPackage,
      );
      if (!mounted) return;
      setState(() {
        _accessEnabled = page.accessEnabled;
        _enabled = page.enabled;
        _total = page.total;
        _applications = page.applications;
        if (reset) {
          _items
            ..clear()
            ..addAll(page.items);
        } else {
          final existing = _items.map((item) => item.id).toSet();
          _items.addAll(page.items.where((item) => existing.add(item.id)));
        }
        _loading = false;
        _loadingMore = false;
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _error = '$error';
        _loading = false;
        _loadingMore = false;
      });
    }
  }

  Future<void> _setEnabled(bool value) async {
    if (!value) {
      final applied = await widget.dataService.setNotificationHistoryEnabled(
        false,
      );
      if (mounted) {
        setState(() => _enabled = applied);
        _showMessage('通知历史采集已关闭，已有记录仍保留');
      }
      return;
    }

    if (!_accessEnabled) {
      await widget.dataService.openNotificationHistorySettings();
      _showMessage('请在系统设置中允许 Hinge 访问通知，返回后再开启采集');
      return;
    }

    final applied = await widget.dataService.setNotificationHistoryEnabled(
      true,
    );
    if (!mounted) return;
    setState(() => _enabled = applied);
    if (applied) {
      _showMessage('通知历史采集已开启');
      await _load(reset: true);
    } else {
      _showMessage('通知历史采集未开启，请确认通知访问权限');
    }
  }

  Future<void> _openItem(NotificationHistoryItem item) async {
    final opened = await widget.dataService.openNotificationHistoryItem(item);
    if (!mounted || opened) return;
    _showMessage('无法打开“${item.appName}”，应用可能已被卸载');
  }

  Future<void> _deleteItem(NotificationHistoryItem item) async {
    try {
      final deleted = await widget.dataService.deleteNotificationHistoryItem(
        item.id,
      );
      if (!mounted) return;
      if (!deleted) {
        _showMessage('通知已经不存在');
        await _load(reset: true);
        return;
      }
      _showMessage('已删除通知');
      await _load(reset: true);
    } catch (error) {
      if (mounted) _showMessage('删除通知失败：$error');
    }
  }

  Future<void> _clearHistory() async {
    if (_total == 0) return;
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('清空通知历史？'),
        content: const Text('所有已保存的通知记录都会被删除，且无法恢复。'),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: const Text('取消'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: const Text('清空'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
    try {
      final deleted = await widget.dataService.clearNotificationHistory();
      if (!mounted) return;
      _showMessage('已清空 $deleted 条通知');
      await _load(reset: true);
    } catch (error) {
      if (mounted) _showMessage('清空通知历史失败：$error');
    }
  }

  void _showMessage(String message) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(
        title: const Text('通知历史'),
        actions: [
          IconButton(
            tooltip: '清空通知历史',
            onPressed: _loading || _total == 0 ? null : _clearHistory,
            icon: const Icon(Symbols.delete_rounded),
          ),
          IconButton(
            tooltip: '刷新',
            onPressed: _loading ? null : () => _load(reset: true),
            icon: const Icon(Symbols.refresh_rounded),
          ),
        ],
      ),
      body: _loading && _items.isEmpty
          ? const Center(child: CircularProgressIndicator())
          : NotificationListener<ScrollNotification>(
              onNotification: (notification) {
                if (notification.metrics.extentAfter < 700 &&
                    !_loadingMore &&
                    _items.length < _total) {
                  unawaited(_load(reset: false));
                }
                return false;
              },
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
                children: [
                  if (!_accessEnabled) _buildPermissionCard(scheme),
                  if (_accessEnabled && !_enabled) _buildDisabledCard(scheme),
                  if (_accessEnabled && _enabled) ...[
                    _buildSummaryCard(scheme),
                    const SizedBox(height: 12),
                    _buildFilters(scheme),
                    const SizedBox(height: 12),
                    if (_error != null) _buildErrorCard(scheme),
                    if (_items.isEmpty && _error == null)
                      _buildEmptyCard(scheme)
                    else
                      ..._items.map(_buildNotificationTile),
                    if (_loadingMore)
                      const Padding(
                        padding: EdgeInsets.all(20),
                        child: Center(child: CircularProgressIndicator()),
                      ),
                  ],
                ],
              ),
            ),
    );
  }

  Widget _buildPermissionCard(ColorScheme scheme) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  Symbols.notifications_active_rounded,
                  color: scheme.primary,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    '需要通知访问权限',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            const Text(
              'Android 会把通知访问作为系统级授权。Hinge 只读取授权之后收到的通知，并把内容保存在应用私有数据库中；已消失的旧通知无法由系统完整恢复。',
            ),
            const SizedBox(height: 12),
            FilledButton.tonalIcon(
              onPressed: () async {
                await widget.dataService.openNotificationHistorySettings();
                if (mounted) _load(reset: true);
              },
              icon: const Icon(Symbols.settings_rounded),
              label: const Text('打开通知访问设置'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildDisabledCard(ColorScheme scheme) {
    return Card(
      child: Column(
        children: [
          SwitchListTile.adaptive(
            secondary: Icon(
              Symbols.notifications_rounded,
              color: scheme.primary,
            ),
            title: const Text('收集应用通知'),
            subtitle: const Text('开启后记录新收到的应用通知，并同步给已连接的 Windows'),
            value: false,
            onChanged: _setEnabled,
          ),
          const Padding(
            padding: EdgeInsets.fromLTRB(16, 0, 16, 16),
            child: Align(
              alignment: Alignment.centerLeft,
              child: Text('通知访问已允许，但历史采集仍处于关闭状态。'),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildSummaryCard(ColorScheme scheme) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
        child: Row(
          children: [
            Icon(Symbols.history_rounded, color: scheme.primary),
            const SizedBox(width: 12),
            Expanded(
              child: Text(
                '共 $_total 条通知 · 已加载 ${_items.length} 条',
                style: Theme.of(context).textTheme.bodyLarge,
              ),
            ),
            Switch.adaptive(value: _enabled, onChanged: _setEnabled),
          ],
        ),
      ),
    );
  }

  Widget _buildFilters(ColorScheme scheme) {
    final applications = <NotificationHistoryApplication>[
      const NotificationHistoryApplication(packageName: '', appName: '全部应用'),
      ..._applications,
    ];
    return Card(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 4, 12, 4),
        child: Column(
          children: [
            DropdownButtonFormField<bool>(
              initialValue: _ascending,
              decoration: const InputDecoration(
                labelText: '时间顺序',
                border: InputBorder.none,
              ),
              items: const [
                DropdownMenuItem(value: true, child: Text('时间（早到晚）')),
                DropdownMenuItem(value: false, child: Text('时间（晚到早）')),
              ],
              onChanged: (value) {
                if (value == null || value == _ascending) return;
                setState(() => _ascending = value);
                _load(reset: true);
              },
            ),
            DropdownButtonFormField<String>(
              initialValue: _selectedPackage,
              decoration: const InputDecoration(
                labelText: '应用筛选',
                border: InputBorder.none,
              ),
              items: applications
                  .map(
                    (app) => DropdownMenuItem<String>(
                      value: app.packageName,
                      child: Text(
                        app.packageName.isEmpty
                            ? app.appName
                            : '${app.appName}（${app.count}）',
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                  )
                  .toList(),
              onChanged: (value) {
                final next = value ?? '';
                if (next == _selectedPackage) return;
                setState(() => _selectedPackage = next);
                _load(reset: true);
              },
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildNotificationTile(NotificationHistoryItem item) {
    final timestamp = item.timestamp <= 0
        ? '时间未知'
        : DateTime.fromMillisecondsSinceEpoch(item.timestamp).toLocal();
    final timeText = timestamp is DateTime
        ? '${timestamp.year}/${timestamp.month}/${timestamp.day} '
              '${timestamp.hour.toString().padLeft(2, '0')}:${timestamp.minute.toString().padLeft(2, '0')}'
        : '$timestamp';
    return Card(
      margin: const EdgeInsets.only(bottom: 10),
      child: ListTile(
        contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 6),
        leading: _buildIcon(item.iconBase64),
        title: Text(
          item.title.isEmpty ? item.appName : '${item.appName} · ${item.title}',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
        ),
        subtitle: Padding(
          padding: const EdgeInsets.only(top: 4),
          child: Text(
            '${item.content.isEmpty ? '（无正文）' : item.content}\n$timeText',
            maxLines: 5,
            overflow: TextOverflow.ellipsis,
          ),
        ),
        isThreeLine: true,
        trailing: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (_isChatApp(item.packageName))
              IconButton(
                tooltip: '打开应用',
                onPressed: () => _openItem(item),
                icon: Icon(
                  Symbols.open_in_new_rounded,
                  color: Theme.of(context).colorScheme.primary,
                ),
              ),
            IconButton(
              tooltip: '删除通知',
              onPressed: () => _deleteItem(item),
              icon: const Icon(Symbols.delete_rounded),
            ),
          ],
        ),
        onTap: () => _openItem(item),
      ),
    );
  }

  Widget _buildIcon(String encoded) {
    if (encoded.isNotEmpty) {
      try {
        return ClipRRect(
          borderRadius: BorderRadius.circular(10),
          child: Image.memory(
            base64Decode(encoded),
            width: 44,
            height: 44,
            fit: BoxFit.contain,
            gaplessPlayback: true,
          ),
        );
      } on FormatException {
        // Use the neutral icon below.
      }
    }
    return CircleAvatar(
      backgroundColor: Theme.of(context).colorScheme.primaryContainer,
      foregroundColor: Theme.of(context).colorScheme.onPrimaryContainer,
      child: const Icon(Symbols.notifications_rounded),
    );
  }

  Widget _buildEmptyCard(ColorScheme scheme) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(28),
        child: Column(
          children: [
            Icon(
              Symbols.notifications_none_rounded,
              size: 42,
              color: scheme.primary,
            ),
            const SizedBox(height: 10),
            const Text('暂时没有符合条件的通知'),
          ],
        ),
      ),
    );
  }

  Widget _buildErrorCard(ColorScheme scheme) {
    return Card(
      color: scheme.errorContainer,
      child: ListTile(
        leading: Icon(
          Symbols.error_outline_rounded,
          color: scheme.onErrorContainer,
        ),
        title: Text(
          '读取通知历史失败',
          style: TextStyle(color: scheme.onErrorContainer),
        ),
        subtitle: Text(
          _error ?? '',
          style: TextStyle(color: scheme.onErrorContainer),
        ),
      ),
    );
  }

  static bool _isChatApp(String packageName) =>
      packageName == 'com.tencent.mm' || packageName == 'com.tencent.mobileqq';
}

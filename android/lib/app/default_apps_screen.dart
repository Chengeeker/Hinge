import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:material_symbols_icons/symbols.dart';

import '../core/workspace_data_service.dart';

/// Chooses the Android activity used when the user taps a received-file
/// notification. It does not replace normal file opening elsewhere in Hinge.
class DefaultAppsScreen extends StatefulWidget {
  final WorkspaceDataService dataService;

  const DefaultAppsScreen({super.key, required this.dataService});

  @override
  State<DefaultAppsScreen> createState() => _DefaultAppsScreenState();
}

class _DefaultAppsScreenState extends State<DefaultAppsScreen> {
  static const _types = <String, String>{
    'image': '图片',
    'video': '视频',
    'file': '文件',
  };

  final Map<String, String?> _selectedPackages = <String, String?>{};
  final Map<String, Map<String, String>> _selectedOptions =
      <String, Map<String, String>>{};
  final Map<String, ImageProvider<Object>> _iconProviders =
      <String, ImageProvider<Object>>{};
  final Set<String> _loading = <String>{};

  @override
  void initState() {
    super.initState();
    for (final type in _types.keys) {
      _loadSelection(type);
    }
  }

  Future<void> _loadSelection(String type) async {
    final packageName = await widget.dataService.defaultApp(type);
    if (!mounted) return;
    setState(() => _selectedPackages[type] = packageName);
    if (packageName == null) return;
    final options = await widget.dataService.defaultAppOptions(type);
    final option = options.where((item) => item['packageName'] == packageName);
    if (!mounted || option.isEmpty) return;
    setState(() => _selectedOptions[type] = option.first);
  }

  Future<void> _choose(String type) async {
    if (_loading.contains(type)) return;
    setState(() => _loading.add(type));
    final options = await widget.dataService.defaultAppOptions(type);
    if (!mounted) return;
    setState(() => _loading.remove(type));

    final selected = await showModalBottomSheet<Map<String, String>?>(
      context: context,
      showDragHandle: true,
      builder: (sheetContext) {
        final current = _selectedPackages[type];
        return SafeArea(
          child: ListView(
            shrinkWrap: true,
            padding: const EdgeInsets.only(bottom: 16),
            children: [
              ListTile(
                title: const Text('系统询问'),
                subtitle: const Text('每次点击通知时选择打开方式'),
                leading: Icon(
                  Symbols.help_outline_rounded,
                  color: Theme.of(sheetContext).colorScheme.primary,
                ),
                trailing: current == null
                    ? const Icon(Symbols.check_rounded)
                    : null,
                onTap: () => Navigator.pop(sheetContext, <String, String>{}),
              ),
              for (final option in options)
                ListTile(
                  title: Text(option['label'] ?? '未命名应用'),
                  subtitle: Text(option['packageName'] ?? ''),
                  leading: _buildAppIcon(option, sheetContext),
                  trailing: option['packageName'] == current
                      ? const Icon(Symbols.check_rounded)
                      : null,
                  onTap: () => Navigator.pop(sheetContext, option),
                ),
              if (options.isEmpty)
                const Padding(
                  padding: EdgeInsets.fromLTRB(24, 8, 24, 24),
                  child: Text('没有找到可处理此类型文件的应用。'),
                ),
            ],
          ),
        );
      },
    );
    if (!mounted || selected == null) return;
    final packageName = selected['packageName'];
    await widget.dataService.setDefaultApp(type, packageName);
    if (!mounted) return;
    setState(() {
      _selectedPackages[type] = packageName?.isEmpty == true
          ? null
          : packageName;
      _selectedOptions[type] = selected;
    });
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Scaffold(
      appBar: AppBar(title: const Text('默认应用')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
        children: [
          Text(
            '仅用于点击“收到文件”通知时的打开方式。Hinge 的其他文件预览和系统默认行为不受影响。',
            style: Theme.of(context).textTheme.bodyLarge,
          ),
          const SizedBox(height: 16),
          Card(
            clipBehavior: Clip.antiAlias,
            child: Column(
              children: [
                for (var index = 0; index < _types.length; index++) ...[
                  if (index > 0) const Divider(height: 1),
                  _buildTypeTile(_types.keys.elementAt(index), scheme),
                ],
              ],
            ),
          ),
          const SizedBox(height: 12),
          Text(
            '如果未指定应用，点击通知时会由 Android 显示可用的打开方式。',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
        ],
      ),
    );
  }

  Widget _buildTypeTile(String type, ColorScheme scheme) {
    final selected = _selectedOptions[type];
    final title = _types[type]!;
    return ListTile(
      leading: selected?['packageName']?.isNotEmpty == true
          ? _buildAppIcon(selected!, context)
          : Icon(_iconFor(type), color: scheme.primary),
      title: Text(title),
      subtitle: Text(
        selected?['label'] ??
            (_selectedPackages[type] == null ? '系统询问' : '已选择应用'),
      ),
      trailing: _loading.contains(type)
          ? const SizedBox(
              width: 20,
              height: 20,
              child: CircularProgressIndicator(strokeWidth: 2),
            )
          : const Icon(Symbols.chevron_right_rounded),
      onTap: () => _choose(type),
    );
  }

  IconData _iconFor(String type) {
    switch (type) {
      case 'image':
        return Symbols.image_rounded;
      case 'video':
        return Symbols.movie_rounded;
      default:
        return Symbols.description_rounded;
    }
  }

  Widget _buildAppIcon(Map<String, String> option, BuildContext context) {
    final provider = _iconProviderFor(option);
    if (provider != null) {
      return ClipRRect(
        borderRadius: BorderRadius.circular(10),
        child: Image(
          image: provider,
          width: 40,
          height: 40,
          fit: BoxFit.contain,
          gaplessPlayback: true,
        ),
      );
    }
    return _fallbackAppIcon(context);
  }

  ImageProvider<Object>? _iconProviderFor(Map<String, String> option) {
    final encoded = option['iconBase64']?.trim() ?? '';
    if (encoded.isEmpty) return null;
    final packageName = option['packageName'] ?? '';
    final cacheKey = '$packageName:$encoded';
    final cached = _iconProviders[cacheKey];
    if (cached != null) return cached;
    try {
      final provider = MemoryImage(base64Decode(encoded));
      _iconProviders[cacheKey] = provider;
      return provider;
    } on FormatException {
      // Fall through to the neutral application glyph for a malformed or
      // unavailable launcher icon returned by an OEM package manager.
      return null;
    }
  }

  Widget _fallbackAppIcon(BuildContext context) =>
      Icon(Symbols.apps_rounded, color: Theme.of(context).colorScheme.primary);
}

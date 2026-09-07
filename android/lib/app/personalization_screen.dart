import 'package:flutter/material.dart';
import 'package:material_symbols_icons/symbols.dart';

import '../core/workspace_state.dart';

class PersonalizationScreen extends StatelessWidget {
  final WorkspaceState state;
  final bool isDesktop;
  final bool hapticFeedbackEnabled;
  final Future<void> Function(bool enabled) onHapticFeedbackChanged;

  const PersonalizationScreen({
    super.key,
    required this.state,
    required this.isDesktop,
    required this.hapticFeedbackEnabled,
    required this.onHapticFeedbackChanged,
  });

  static const _presets = <({String name, int color})>[
    (name: '经典红', color: 0xFFB3261E),
    (name: '活力橙', color: 0xFFFF8A00),
    (name: '极光蓝', color: 0xFF1976D2),
    (name: '翡翠绿', color: 0xFF2E7D32),
    (name: '优雅紫', color: 0xFF6750A4),
    (name: '樱花粉', color: 0xFFE91E63),
    (name: '青碧色', color: 0xFF009688),
    (name: '经典黑灰', color: 0xFF546E7A),
  ];

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: state,
      builder: (context, _) {
        final scheme = Theme.of(context).colorScheme;
        return Theme(
          // Keep the rounded settings surfaces free from the sharp rectangular
          // ink expansion that Flutter can paint during a long press.
          data: Theme.of(context)
              .copyWith(splashFactory: NoSplash.splashFactory),
          child: Scaffold(
            appBar: AppBar(
              title: const Text('个性化'),
              leading: IconButton(
                tooltip: '返回设置',
                icon: const Icon(Symbols.arrow_back_rounded),
                onPressed: () => Navigator.maybePop(context),
              ),
            ),
            body: ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                _buildThemeCard(context, scheme),
                const SizedBox(height: 16),
                _buildColorCard(context, scheme),
                const SizedBox(height: 16),
                _buildFontCard(context, scheme),
                const SizedBox(height: 16),
                _buildNavigationCard(context, scheme),
                const SizedBox(height: 16),
                _buildInteractionCard(context, scheme),
              ],
            ),
          ),
        );
      },
    );
  }

  Widget _sectionCard({
    required BuildContext context,
    required ColorScheme scheme,
    required String title,
    required Widget child,
  }) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 18, 16, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              title,
              style: Theme.of(context).textTheme.titleMedium?.copyWith(
                color: scheme.primary,
                fontWeight: FontWeight.w700,
              ),
            ),
            const SizedBox(height: 8),
            child,
          ],
        ),
      ),
    );
  }

  Widget _buildThemeCard(BuildContext context, ColorScheme scheme) {
    return _sectionCard(
      context: context,
      scheme: scheme,
      title: '明暗模式',
      child: Column(
        children: [
          RadioGroup<AppThemePreference>(
            groupValue: state.themePreference,
            onChanged: (value) {
              if (value != null) state.setThemePreference(value);
            },
            child: const Column(
              children: [
                RadioListTile<AppThemePreference>(
                  value: AppThemePreference.system,
                  title: Text('跟随系统'),
                  subtitle: Text('自动匹配系统深色 / 浅色设置'),
                  contentPadding: EdgeInsets.zero,
                ),
                RadioListTile<AppThemePreference>(
                  value: AppThemePreference.light,
                  title: Text('浅色模式'),
                  contentPadding: EdgeInsets.zero,
                ),
                RadioListTile<AppThemePreference>(
                  value: AppThemePreference.dark,
                  title: Text('深色模式'),
                  contentPadding: EdgeInsets.zero,
                ),
              ],
            ),
          ),
          const Divider(height: 1),
          SwitchListTile.adaptive(
            contentPadding: EdgeInsets.zero,
            secondary: const Icon(Symbols.contrast_rounded),
            title: const Text('纯黑深色模式'),
            subtitle: const Text('深色模式下使用纯黑背景，适合 OLED 屏幕'),
            value: state.pureBlackDarkMode,
            onChanged: state.setPureBlackDarkMode,
          ),
        ],
      ),
    );
  }

  Widget _buildColorCard(BuildContext context, ColorScheme scheme) {
    return _sectionCard(
      context: context,
      scheme: scheme,
      title: '色彩方案',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SwitchListTile.adaptive(
            contentPadding: EdgeInsets.zero,
            secondary: const Icon(Symbols.auto_awesome_rounded),
            title: const Text('壁纸动态取色'),
            subtitle: Text(
              isDesktop
                  ? 'Windows 使用系统强调色；安卓端不可用'
                  : '开启后从 Android 壁纸提取系统色；关闭后使用下方预置配色',
            ),
            value: state.dynamicColorEnabled,
            onChanged: isDesktop ? null : state.setDynamicColorEnabled,
          ),
          const SizedBox(height: 8),
          if (state.dynamicColorEnabled)
            ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Icon(Symbols.wallpaper_rounded, color: scheme.primary),
              title: const Text('正在使用壁纸色彩'),
              subtitle: const Text('关闭动态取色后可选择下方预置主题'),
            )
          else ...[
            Text('预置主题配色', style: Theme.of(context).textTheme.bodyLarge),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final preset in _presets)
                  ChoiceChip(
                    selected: state.seedColor == preset.color,
                    avatar: CircleAvatar(
                      radius: 9,
                      backgroundColor: Color(preset.color),
                    ),
                    label: Text(preset.name),
                    onSelected: isDesktop
                        ? null
                        : (_) {
                            state.setSeedColor(preset.color);
                          },
                  ),
              ],
            ),
          ],
        ],
      ),
    );
  }

  Widget _buildFontCard(BuildContext context, ColorScheme scheme) {
    return _sectionCard(
      context: context,
      scheme: scheme,
      title: '字体粗细',
      child: ListTile(
        contentPadding: EdgeInsets.zero,
        leading: Icon(Symbols.format_bold_rounded, color: scheme.primary),
        title: const Text('自定义应用字体粗细'),
        subtitle: Text('当前：${_fontWeightLabel(state.fontWeightLevel)}'),
        trailing: const Icon(Symbols.chevron_right_rounded),
        onTap: () => _showFontWeightPicker(context),
      ),
    );
  }

  Widget _buildNavigationCard(BuildContext context, ColorScheme scheme) {
    return _sectionCard(
      context: context,
      scheme: scheme,
      title: '导航布局',
      child: Column(
        children: [
          RadioGroup<AppNavigationStyle>(
            groupValue: isDesktop
                ? state.navigationStyle
                : AppNavigationStyle.bottom,
            onChanged: (value) {
              if (value != null && isDesktop) state.setNavigationStyle(value);
            },
            child: Column(
              children: [
                if (isDesktop)
                  const RadioListTile<AppNavigationStyle>(
                    value: AppNavigationStyle.adaptive,
                    title: Text('自适应布局'),
                    subtitle: Text('桌面使用侧边栏'),
                    contentPadding: EdgeInsets.zero,
                  ),
                if (isDesktop)
                  const RadioListTile<AppNavigationStyle>(
                    value: AppNavigationStyle.sidebar,
                    title: Text('侧边栏布局'),
                    contentPadding: EdgeInsets.zero,
                  ),
                const RadioListTile<AppNavigationStyle>(
                  value: AppNavigationStyle.bottom,
                  title: Text('底栏布局'),
                  subtitle: Text('安卓端固定使用底部导航'),
                  contentPadding: EdgeInsets.zero,
                ),
              ],
            ),
          ),
          SwitchListTile.adaptive(
            contentPadding: EdgeInsets.zero,
            secondary: const Icon(Symbols.view_carousel_rounded),
            title: const Text('悬浮胶囊底栏'),
            subtitle: const Text('使用悬浮的 Material 3 Expressive 胶囊导航'),
            value: state.floatingCapsuleNavigation,
            onChanged: state.setFloatingCapsuleNavigation,
          ),
        ],
      ),
    );
  }

  Widget _buildInteractionCard(BuildContext context, ColorScheme scheme) {
    return _sectionCard(
      context: context,
      scheme: scheme,
      title: '交互反馈',
      child: isDesktop
          ? ListTile(
              contentPadding: EdgeInsets.zero,
              leading: Icon(
                Symbols.desktop_windows_rounded,
                color: scheme.primary,
              ),
              title: const Text('震动反馈'),
              subtitle: const Text('Windows 使用系统输入反馈'),
              trailing: const Text('不适用'),
            )
          : SwitchListTile.adaptive(
              contentPadding: EdgeInsets.zero,
              secondary: const Icon(Symbols.vibration_rounded),
              title: const Text('震动反馈'),
              subtitle: const Text('点击按钮、切换页面时提供轻微触感反馈'),
              value: hapticFeedbackEnabled,
              onChanged: (value) {
                onHapticFeedbackChanged(value);
              },
            ),
    );
  }

  Future<void> _showFontWeightPicker(BuildContext context) async {
    await showDialog<void>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('选择字体粗细'),
        content: RadioGroup<int>(
          groupValue: state.fontWeightLevel,
          onChanged: (value) {
            if (value != null) state.setFontWeightLevel(value);
          },
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              for (final level in const [1, 0, 2, 3, 4])
                RadioListTile<int>(
                  value: level,
                  title: Text(_fontWeightLabel(level)),
                  subtitle: Text(_fontWeightDescription(level)),
                  contentPadding: EdgeInsets.zero,
                ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text('完成'),
          ),
        ],
      ),
    );
  }

  String _fontWeightLabel(int level) {
    switch (level) {
      case 1:
        return '偏细';
      case 0:
        return '默认';
      case 2:
        return '中等';
      case 3:
        return '偏粗';
      case 4:
        return '加粗';
      default:
        return '默认';
    }
  }

  String _fontWeightDescription(int level) {
    switch (level) {
      case 1:
        return '轻盈精炼视觉，适合大字号阅读';
      case 0:
        return '官方标准字重，最佳均衡排版';
      case 2:
        return '适度加深笔触，更清晰明朗';
      case 3:
        return '粗体质感，信息层级更醒目';
      case 4:
        return '极致浓郁，强调视觉冲击力';
      default:
        return '';
    }
  }
}

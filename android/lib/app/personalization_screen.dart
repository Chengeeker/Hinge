import 'dart:ui' show ImageFilter;

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
        final theme = Theme.of(context);
        return Theme(
          data: theme.copyWith(
            splashFactory: NoSplash.splashFactory,
            listTileTheme: theme.listTileTheme.copyWith(
              contentPadding: const EdgeInsets.symmetric(
                horizontal: 16,
                vertical: 3,
              ),
              minVerticalPadding: 8,
              minLeadingWidth: 24,
              horizontalTitleGap: 16,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(16),
              ),
              titleTextStyle: _textStyle(
                context,
                16,
                FontWeight.w600,
                scheme.onSurface,
              ),
              subtitleTextStyle: _textStyle(
                context,
                14,
                FontWeight.w400,
                scheme.onSurfaceVariant,
              ).copyWith(height: 1.4),
            ),
          ),
          child: Scaffold(
            extendBodyBehindAppBar: !isDesktop,
            appBar: AppBar(
              title: const Text('个性化'),
              titleTextStyle: _textStyle(
                context,
                22,
                FontWeight.w700,
                scheme.onSurface,
              ),
              backgroundColor: isDesktop
                  ? scheme.surface
                  : scheme.surface.withValues(alpha: 0.45),
              surfaceTintColor: Colors.transparent,
              elevation: 0,
              scrolledUnderElevation: 0,
              flexibleSpace: isDesktop ? null : _headerBackdrop(scheme),
              leading: IconButton(
                tooltip: '返回设置',
                icon: const Icon(Symbols.arrow_back_rounded),
                onPressed: () => Navigator.maybePop(context),
              ),
            ),
            body: ListView(
              padding: EdgeInsets.fromLTRB(
                16,
                12 +
                    (isDesktop
                        ? 0
                        : MediaQuery.paddingOf(context).top + kToolbarHeight),
                16,
                12 + MediaQuery.viewPaddingOf(context).bottom,
              ),
              children: [
                _buildThemeCard(context, scheme),
                const SizedBox(height: 16),
                _buildColorCard(context, scheme),
                const SizedBox(height: 16),
                _buildFontCard(context, scheme),
                const SizedBox(height: 16),
                _buildNavigationCard(context, scheme),
                const SizedBox(height: 16),
                _buildLiquidGlassCard(context, scheme),
                const SizedBox(height: 16),
                _buildInteractionCard(context, scheme),
              ],
            ),
          ),
        );
      },
    );
  }

  TextStyle _textStyle(
    BuildContext context,
    double size,
    FontWeight weight,
    Color color,
  ) {
    final adjusted = ((weight.value + state.fontWeightDelta) / 100).round() - 1;
    return (Theme.of(context).textTheme.bodyLarge ?? const TextStyle())
        .copyWith(
          fontSize: size,
          fontWeight: FontWeight.values[adjusted.clamp(0, 8)],
          color: color,
          height: 1.35,
          letterSpacing: 0,
        );
  }

  Widget _headerBackdrop(ColorScheme scheme) => ClipRect(
    child: BackdropFilter(
      filter: ImageFilter.blur(sigmaX: 20, sigmaY: 20),
      child: DecoratedBox(
        decoration: BoxDecoration(
          gradient: LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [
              scheme.surface.withValues(alpha: 0.95),
              scheme.surface.withValues(alpha: 0),
            ],
          ),
          border: Border(
            bottom: BorderSide(
              color: scheme.outlineVariant.withValues(alpha: 0.24),
              width: 0.5,
            ),
          ),
        ),
        child: const SizedBox.expand(),
      ),
    ),
  );

  Widget _sectionCard({
    required BuildContext context,
    required ColorScheme scheme,
    required String title,
    required Widget child,
    double headingBottomPadding = 4,
  }) {
    return Card(
      margin: EdgeInsets.zero,
      color: scheme.surface == Colors.black
          ? scheme.surfaceContainer
          : scheme.surfaceContainerLow,
      elevation: 0,
      shadowColor: Colors.transparent,
      surfaceTintColor: Colors.transparent,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(24)),
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: EdgeInsets.fromLTRB(16, 14, 16, headingBottomPadding),
            child: Text(
              title,
              style: _textStyle(
                context,
                15,
                FontWeight.w700,
                scheme.onSurfaceVariant,
              ),
            ),
          ),
          child,
        ],
      ),
    );
  }

  Widget _buildThemeCard(BuildContext context, ColorScheme scheme) {
    return _sectionCard(
      context: context,
      scheme: scheme,
      title: '明暗模式',
      headingBottomPadding: 6,
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
                  contentPadding: EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 2,
                  ),
                ),
                RadioListTile<AppThemePreference>(
                  value: AppThemePreference.light,
                  title: Text('浅色模式'),
                  contentPadding: EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 2,
                  ),
                ),
                RadioListTile<AppThemePreference>(
                  value: AppThemePreference.dark,
                  title: Text('深色模式'),
                  contentPadding: EdgeInsets.symmetric(
                    horizontal: 16,
                    vertical: 2,
                  ),
                ),
              ],
            ),
          ),
          SwitchListTile(
            secondary: Icon(Symbols.contrast_rounded, color: scheme.primary),
            title: const Text('纯黑深色模式'),
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
          SwitchListTile(
            secondary: Icon(
              Symbols.auto_awesome_rounded,
              color: scheme.primary,
            ),
            title: const Text('壁纸动态取色'),
            subtitle: isDesktop ? const Text('Windows 使用系统强调色；安卓端不可用') : null,
            value: state.dynamicColorEnabled,
            onChanged: isDesktop ? null : state.setDynamicColorEnabled,
          ),
          if (state.dynamicColorEnabled)
            ListTile(
              leading: Icon(Symbols.wallpaper_rounded, color: scheme.primary),
              title: const Text('正在使用壁纸色彩'),
            )
          else ...[
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 10),
              child: Text(
                'Material 3 预置主题色',
                style: _textStyle(
                  context,
                  14,
                  FontWeight.w400,
                  scheme.onSurfaceVariant,
                ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 12),
              child: Wrap(
                spacing: 12,
                runSpacing: 12,
                children: [
                  for (final preset in _presets)
                    ChoiceChip(
                      selected: state.seedColor == preset.color,
                      avatar: CircleAvatar(
                        radius: 10,
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
            ),
          ],
          const SizedBox(height: 8),
        ],
      ),
    );
  }

  Widget _buildFontCard(BuildContext context, ColorScheme scheme) {
    return _sectionCard(
      context: context,
      scheme: scheme,
      title: '字体粗细',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Builder(
            builder: (tileContext) => ListTile(
              leading: Icon(Symbols.format_bold_rounded, color: scheme.primary),
              title: const Text('自定义应用字体粗细'),
              subtitle: Text('当前：${_fontWeightLabel(state.fontWeightLevel)}'),
              trailing: const Icon(Symbols.chevron_right_rounded),
              onTap: () => _showFontWeightPicker(tileContext),
            ),
          ),
          Container(
            margin: const EdgeInsets.fromLTRB(16, 12, 16, 14),
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: scheme.surfaceContainerHighest.withValues(alpha: 0.3),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '效果预览 (${_fontWeightLabel(state.fontWeightLevel)})',
                  style: _textStyle(
                    context,
                    11.5,
                    FontWeight.w600,
                    scheme.primary,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  'Hinge 跨设备工作区，让手机与电脑协同更自然。',
                  style: _textStyle(
                    context,
                    14,
                    FontWeight.w400,
                    scheme.onSurface,
                  ).copyWith(height: 1.4),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildNavigationCard(BuildContext context, ColorScheme scheme) {
    return _sectionCard(
      context: context,
      scheme: scheme,
      title: isDesktop ? '导航布局' : '底部导航',
      child: Column(
        children: [
          if (isDesktop)
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
                      contentPadding: EdgeInsets.symmetric(
                        horizontal: 16,
                        vertical: 2,
                      ),
                    ),
                  if (isDesktop)
                    const RadioListTile<AppNavigationStyle>(
                      value: AppNavigationStyle.sidebar,
                      title: Text('侧边栏布局'),
                      contentPadding: EdgeInsets.symmetric(
                        horizontal: 16,
                        vertical: 2,
                      ),
                    ),
                  const RadioListTile<AppNavigationStyle>(
                    value: AppNavigationStyle.bottom,
                    title: Text('底栏布局'),
                    subtitle: Text('安卓端固定使用底部导航'),
                    contentPadding: EdgeInsets.symmetric(
                      horizontal: 16,
                      vertical: 2,
                    ),
                  ),
                ],
              ),
            ),
          SwitchListTile(
            secondary: Icon(Symbols.dock_rounded, color: scheme.primary),
            title: const Text('使用悬浮底栏'),
            subtitle: const Text('居中显示悬浮式导航栏'),
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
              leading: Icon(
                Symbols.desktop_windows_rounded,
                color: scheme.primary,
              ),
              title: const Text('震动反馈'),
              subtitle: const Text('Windows 使用系统输入反馈'),
              trailing: const Text('不适用'),
            )
          : SwitchListTile(
              secondary: Icon(Symbols.vibration_rounded, color: scheme.primary),
              title: const Text('震动反馈'),
              value: hapticFeedbackEnabled,
              onChanged: (value) {
                onHapticFeedbackChanged(value);
              },
            ),
    );
  }

  Widget _buildLiquidGlassCard(BuildContext context, ColorScheme scheme) {
    return _sectionCard(
      context: context,
      scheme: scheme,
      title: '显示效果',
      child: SwitchListTile(
        secondary: Icon(Symbols.blur_on_rounded, color: scheme.primary),
        title: const Text('使用液态玻璃效果'),
        subtitle: const Text('控制液态玻璃控件；当前应用于悬浮底栏'),
        value: state.useLiquidGlass,
        onChanged: state.setUseLiquidGlass,
      ),
    );
  }

  Future<void> _showFontWeightPicker(BuildContext context) async {
    await showDialog<void>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('选择字体粗细'),
        contentPadding: const EdgeInsets.symmetric(vertical: 12),
        content: RadioGroup<int>(
          groupValue: state.fontWeightLevel,
          onChanged: (value) {
            if (value == null) return;
            state.setFontWeightLevel(value);
            Navigator.pop(dialogContext);
          },
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              for (final level in const [1, 0, 2, 3, 4])
                RadioListTile<int>(
                  value: level,
                  title: Text(_fontWeightLabel(level)),
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: 20,
                    vertical: 2,
                  ),
                ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text('取消'),
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
}

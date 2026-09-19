import 'package:flutter/material.dart';
import 'package:material_symbols_icons/symbols.dart';

/// Single source of truth for the Android workspace navigation labels and
/// icons. Rail, drawer and bottom navigation all derive from this list so a
/// label or icon cannot drift between form factors.
class HingeNavigationDestinationSpec {
  final IconData icon;
  final String label;
  final String desktopBarLabel;
  final IconData? mobileBarIcon;
  final String? mobileBarLabel;

  const HingeNavigationDestinationSpec({
    required this.icon,
    required this.label,
    required this.desktopBarLabel,
    this.mobileBarIcon,
    this.mobileBarLabel,
  });

  NavigationRailDestination toRailDestination() => NavigationRailDestination(
    icon: Icon(icon),
    selectedIcon: Icon(icon, fill: 1),
    label: Text(label),
  );

  NavigationDrawerDestination toDrawerDestination() =>
      NavigationDrawerDestination(
        icon: Icon(icon),
        selectedIcon: Icon(icon, fill: 1),
        label: Text(label),
      );

  NavigationDestination toBarDestination({required bool desktop}) {
    final barIcon = desktop ? icon : (mobileBarIcon ?? icon);
    final barLabel = desktop ? desktopBarLabel : (mobileBarLabel ?? label);
    return NavigationDestination(
      icon: Icon(barIcon),
      selectedIcon: Icon(barIcon, fill: 1),
      label: barLabel,
    );
  }
}

const hingeNavigationDestinationSpecs = <HingeNavigationDestinationSpec>[
  HingeNavigationDestinationSpec(
    icon: Symbols.home_rounded,
    label: '首页',
    desktopBarLabel: '首页',
  ),
  HingeNavigationDestinationSpec(
    icon: Symbols.devices_rounded,
    label: '已连接的机型',
    desktopBarLabel: '机型',
  ),
  HingeNavigationDestinationSpec(
    icon: Symbols.add_link_rounded,
    label: '连接设备',
    desktopBarLabel: '连接',
  ),
  HingeNavigationDestinationSpec(
    icon: Symbols.note_rounded,
    label: '笔记代办',
    desktopBarLabel: '笔记',
    mobileBarIcon: Symbols.workspaces_rounded,
    mobileBarLabel: '工作区',
  ),
  HingeNavigationDestinationSpec(
    icon: Symbols.calendar_month_rounded,
    label: '日历',
    desktopBarLabel: '日历',
  ),
  HingeNavigationDestinationSpec(
    icon: Symbols.photo_library_rounded,
    label: '相册',
    desktopBarLabel: '相册',
  ),
  HingeNavigationDestinationSpec(
    icon: Symbols.settings_rounded,
    label: '设置',
    desktopBarLabel: '设置',
  ),
];

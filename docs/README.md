# Hinge 项目文档索引

项目开发决策与历史修复记录以 `D:\App\开发文档\Hinge\README.md` 为主；这里按主题索引验收报告和截图证据。报告保留原文件名和历史结论，不移动、不删除旧记录。遇到更正过的报告时，以报告中明确标注的最终修复版本及开发日志中的后续记录为准。

## 当前基线

- [Windows M3 当前基线与旧安装替换记录](HINGE_WINDOWS_M3_BASELINE_20261011.md)
- [安装器默认路径修复验收](HINGE_INSTALLER_DEFAULT_PATH_FIX_20261011.md)
- 回退快照：`D:\App\Hinge\publish\rollback\pre-baseline-replace-d-hinge-20261011\`

## M3 迁移与整体视觉

- [Windows M3 迁移报告](HINGE_WINDOWS_M3_MIGRATION_REPORT.md)
- [M3 V3 定向视觉修复报告](HINGE_M3_VISUAL_V3_REPORT.md)
- [M3 V4 视觉验收报告](HINGE_M3_V4_ACCEPTANCE.md)
- [HarmonyOS Sans SC 字体验收](HARMONYOS_SANS_WINDOWS_FONT_ACCEPTANCE_20261010.md)

## 页面布局与组件修复

- [个性化页自定义字体导入](HINGE_WINDOWS_CUSTOM_FONT_IMPORT_20261011.md)
- [输入框文字垂直居中与产品信息文案](HINGE_WINDOWS_INPUT_TEXT_ALIGNMENT_ACCEPTANCE_20261011.md)
- 首页设备行与历史记录：[报告](HINGE_HOME_DEVICE_HISTORY_LAYOUT_ACCEPTANCE_20261010.md)、[Logo 间距](HINGE_HOME_HISTORY_LOGO_GAP_ACCEPTANCE_20261010.md)、[最大化历史视口](HINGE_HOME_TRANSFER_HISTORY_MAXIMIZED_FIX_20261010.md)、[宫格列数与视口补充](HINGE_HOME_HISTORY_VIEWPORT_FILE_GRID_ACCEPTANCE_20261010.md)
- 设置页面统一对齐：[报告](HINGE_SETTINGS_PAGE_ALIGNMENT_ACCEPTANCE_20261010.md)
- 文件品牌标识与通知列表：[报告](HINGE_FILEBRAND_NOTIFICATION_LAYOUT_ACCEPTANCE_20261010.md)
- 未识别品牌默认设备 Logo：[报告](HINGE_BRAND_LOGO_FALLBACK_ACCEPTANCE_20261011.md)
- Windows 未连接状态设备 Logo：[报告](HINGE_WINDOWS_DISCONNECTED_DEVICE_LOGO_20261011.md)
- 融合式窗口标题栏：[报告](HINGE_INTEGRATED_TITLEBAR_ACCEPTANCE_20261010.md)
- 侧栏、窗口按钮与日历：[报告](HINGE_M3_RAIL_CAPTION_CALENDAR_ACCEPTANCE_20261010.md)
- 日历月视图：[报告](HINGE_CALENDAR_M3_ACCEPTANCE_20261010.md)

## 弹窗与交互

- [Windows 首页离线发送保护](HINGE_WINDOWS_OFFLINE_SEND_GUARD_20261011.md)
- [可点击区域手形光标](HINGE_WINDOWS_POINTER_CURSOR_ACCEPTANCE_20261011.md)
- [日历选中态与 M3 弹窗修复](HINGE_M3_CALENDAR_DIALOG_FIX_ACCEPTANCE_20261010.md)
- [M3 主按钮几何与默认态最终修复](HINGE_M3_DIALOG_BUTTON_GEOMETRY_ACCEPTANCE_20261010.md)
- [三处 WinUI 页面定点修复](HINGE_M3_THREE_UI_FIXES_ACCEPTANCE_20261010.md)

## 截图证据

页面截图与图形验收证据位于 [`visual-acceptance/`](visual-acceptance/)。各报告内记录了对应目录、窗口/DPI 条件及未覆盖的测试项；构建通过不等同于所有显示缩放、主题和真实设备均已验收。

## 文档维护约定

- 新的结论更新开发文档的对应专题，故障原因和验证边界写入问题与回归。已被替代的方案归入历史并注明替代关系。
- 新增验收报告时，在本索引添加一条链接，并区分真实截图/运行验证与静态检查。
- 回退前只恢复该回退快照说明的文件，不重置整个工作树。

# Hinge 首页传输记录最大化高度修复（2026-10-10）

## 根因与改动

用户提供的最大化截图证明上一轮的内容根高度差公式没有扩大传输记录列表。该公式依赖 `HomePageContentRoot.ActualHeight`，而滚动视图中的内容根可能被拉伸到视口高度，导致计算结果继续受旧列表高度影响。

现改为使用 `TransferHistoryListView.TransformToVisual(HomePageContentRoot)` 测得列表顶部 Y 坐标，按 `HomeScrollViewer.ViewportHeight - 列表顶部 Y - 卡片底部 Padding - 页面底边距` 求最大高度。窗口、顶部卡片布局和历史记录变化继续触发重算，最低保留 120 DIP。未修改记录数据、操作按钮或传输业务。上一轮相关数值自检不作为实际界面验收证据。

## 验证

- 修改前回退快照：`publish/rollback/pre-home-history-explicit-remaining-height-20261010/`，包含目标 XAML/C#、开发记录及现有 Windows/APK 包。
- 构建、测试、包校验和 GUI 截图结果将在完成后追加；GUI 无法启动时明确标记为未验证，不以静态公式替代截图验收。

## 完成后的实测结果

- Windows Tests：180/180 通过。Windows Release 安装器和便携 ZIP 构建成功；便携 ZIP 共 809 项并全部读取成功，ZIP 内 `Hinge.dll` 与 Release 输出 SHA-256 相同：`00F07744B253606317C88955F0D3A71D8F0F8B3A5470FDBE580E1783076DCDAE`。
- Android APK 按项目规程重建成功，`apksigner` 的 v2/v3 校验通过；SHA-256：`B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`。Windows 安装器 SHA-256：`268AAA46C2E5DC033D511F23DD6EDB95932339EEC93A076AB3B59C488F557DCA`；便携 ZIP SHA-256：`0F348EDA2D1B410C7838E1765C367E9B0415D6DB365EB676CCFF29979E83A1DD`。
- 实际启动隔离候选并最大化首页，主显示器 2048×1104，窗口 DPI 为 120（125%）。截图显示传输记录列表随窗口可用高度明显延伸，视口下方不再留出原截图中的大块空白，列表仍保留内部滚动。截图：`docs/visual-acceptance/20261010-home-history-height/home-maximized-candidate.png`。
- 未验证 100%/150% DPI、窄窗口和深色主题；没有点击删除/清空记录，以免更改本机历史数据。该改动仅调整列表 MaxHeight，没有更改行内容和按钮事件。候选实例已退出；没有主动关闭用户原有实例。
- 安装器 Authenticode 状态为 `NotSigned`，这是当前项目既有发布状态，不代表微软商店签名包。
- 回退副本：`publish/rollback/pre-home-history-explicit-remaining-height-20261010/`。

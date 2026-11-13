# Hinge M3 侧栏、原生窗口按钮、日历定点修复验收

日期：2026-10-10。依据用户附件，以开始本轮时的本地工作树为基线。只修改四个 UI 文件，保留已有未提交修改；没有提交、推送或安装覆盖用户客户端。

## 修改文件

- `windows/Hinge.App/MainWindow.xaml`：原生 NavigationView CompactPaneLength 从 48 改为 72 DIP，订阅 DisplayModeChanged。
- `windows/Hinge.App/MainWindow.xaml.cs`：及时退出侧栏拖柄命中；标题栏拖动区排除实际 Rail 宽度；原生 caption 状态色进行不透明合成。
- `windows/Hinge.App/CalendarPage.xaml`：局部日期 Typography，基于已有 Body 样式，19 DIP Regular。
- `windows/Hinge.App/CalendarPage.xaml.cs`：日期按钮自身 Stretch，44 DIP 固定日期容器、38 DIP 圆形指示器；所有指针状态共用同一个指示器和中心点；选中不改变字重。

未改全局主题系统、ThemeBrushes、传输、BLE、ADB、Android 日历协议或创建/同步流程。没有新增依赖、负边距、TranslateTransform 补偿或透明覆盖控件。

## 根因和修复

| 问题 | 实际观察 | 最小修复 |
| --- | --- | --- |
| 侧栏旧占位/竖线 | 当前基线稳定收起时 ContentFrame 已从 X=48 开始，未复现永久 200 DIP 空白。复现的是收起动画过程中旧拖柄仍在 X=196 Visible/hit=True，直到 PaneClosed 才退出。原生 SplitView 的 PaneRoot 保持 OpenPaneLength 是被裁剪的模板行为，不能单凭 ActualWidth 判定永久占位。 | 监听 IsPaneOpen 即时变化和 DisplayModeChanged，只在 Expanded 且展开时保留拖柄/Canvas 的可见性及命中。原生 NavigationView 负责真实布局，Rail 72 DIP；标题栏排除宽度同步使用 CompactPaneLength。 |
| min/max Hover 黑块 | 状态刷颜色来自 OnSurface，透明度保存在 SolidColorBrush.Opacity。ThemeBrushes.Color 只返回 Color，其缓存刷也未带原始 Opacity；旧 native 属性收到不透明深色 RGB。第一轮仅处理 alpha 没有奏效。 | 在本地标题按钮路径读取语义颜色及资源原始 Opacity，将 Hover/Pressed 合成到当前 Surface，传给系统 alpha=255 的背景色。没有修改共享 ThemeBrushes。 |
| 日期小/中心变化 | 日期使用 Label 字号；Button 的内容设了 Stretch，但 Button 自身仍继承原生 Left/Center，实际高度随事件内容变化，使日期整体垂直移动。旧 Hover 又作用于整个单元格，和日期圆不是同一套视觉范围。 | Button 自身双向 Stretch，日期文字与背景嵌套在同一 44×44 容器，38×38 圆。字体 19 DIP；今天 SemiBold、其他 Regular，选中不重测字重。Hover/Pressed 仅修改该圆背景；今天+选中只保留主题色圆。 |

## 真实运行验收

按侧栏→caption→日历顺序分别构建、启动临时候选并鼠标操作。候选使用临时单实例键，避免关闭用户原 Debug 进程；正式 App.xaml.cs 按字节恢复，SHA-256 `059AD97363528CE945D7A5179AA3AEB7BF72612A7057F590980816A592D79960`。最终包未包含临时键、样例事件、测量日志代码。

实际 XamlRoot.RasterizationScale=1.25。截图 API 输出为逻辑尺寸，不把图片像素尺寸当成 Windows DPI 切换证据。

| 验收项 | 结果/边界 |
| --- | --- |
| 展开/收起 | 鼠标菜单反复切换；稳定收起 ContentFrame X=72。Canvas/Handle 同时 Collapsed、hit=False。无旧 200/299 DIP 额外占位。 |
| 侧栏拖宽后收起/展开 | 鼠标拖至 UIA 299.200012 DIP，收起仍为 72 DIP；重新展开宽度正确。键盘 Home 恢复 200 DIP，焦点框可见。 |
| 窗口缩窄/最大化 | 原生系统菜单 Size 将窗口缩至约 670 DIP，NavigationView Compact 模式及 Overlay Drawer 正常，内容仍从 72 DIP 开始；最大化后亦正常。不是通过截图缩放模拟。 |
| 原生 min/max Hover | 浅色与深色均实际移动鼠标、截图，无原来纯黑方块。 |
| Snap Layouts | 浅色/深色最大化按钮 Hover 均看到系统 Snap 布局弹出；未拖入每个 Snap 区域逐项验证。 |
| 日期 1/7/10/28 | 实际普通、Hover、点击选中检查；一位/两位日期不随状态移动。今天与选中同日只有一个强调圆，不同日可分别显示。 |
| 有事件/全天/跨天 | 临时候选注入明确标注的本地样例，未同步、未写手机。事件内容未挤动日期；保持项目既有彩色普通事件和跨天条。最终包没有样例。 |
| 日期几何测量 | 最终布局中 1/7/10/28 指示器相对按钮中心固定 (32,24) DIP。文字/圆心偏差分别约 (0.32,-0.16)/(0.32,-0.16)/(0.30,-0.16)/(-0.16,-0.16) DIP，包含 125% 像素舍入。改变侧栏宽度后仍一致。 |
| 深色与其他月份日期 | 深色日期/事件可读；前后月份日期保持弱化。 |
| 新建日程 | 打开表单成功，取消返回日历。未向真实手机提交日程。 |
| 导航与窗口能力 | 设置/个性化/日历导航、原生最大化/还原可操作；保留原有系统按钮及窗口消息逻辑。 |

截图有鼠标高亮光晕，它来自验收工具，不属于应用视觉。不要把高亮光晕当作 Hover 状态层。

## 截图

证据目录：`docs/visual-acceptance/20261010-rail-caption-calendar/`。

- 基线：before-expanded-0.jpg、before-collapsed-0.jpg、before-caption-hover-0.jpg、before-calendar-0.jpg。
- 侧栏：rail-expanded-0.jpg、rail-collapsed-0.jpg、rail-narrow-0.jpg、rail-maximized-0.jpg、rail-width-drag-0.jpg、rail-final-dark-collapsed-0.jpg。
- 系统按钮：caption-light-min-hover-0.jpg、caption-light-max-hover-0.jpg、caption-dark-min-hover-0.jpg、caption-dark-max-hover-0.jpg。
- 最终日期布局：calendar-final-events-0.jpg、calendar-final-01-hover-0.jpg、calendar-final-01-selected-0.jpg、calendar-final-dark-0.jpg、calendar-final-dark-hover-0.jpg、calendar-final-today-selected-0.jpg。
- 今天+选中及单日期检查：calendar-today-selected-0.jpg、calendar-07-selected-0.jpg、calendar-28-hover-0.jpg、calendar-28-selected-0.jpg；这些是同轮较早日期候选，最终字体边界居中由 final 截图与 date-measurements.log 证明。
- 新建表单：calendar-create-regression-0.jpg。

早期日期测量日志含 HAlign=Stretch 的文本边界数据；最终 HAlign=Center 结果从首次 delta=0.32,-0.16 开始，不能混用旧结果。部分早期 hover 文件捕获在鼠标离开后，以上最终 Hover 截图为报告依据。

## 构建与包检查

每项候选 Release x64 publish 成功；最终 build_release_windows.ps1 成功，Shell 编译 0 警告/0 错误。四个目标文件 scoped CRLF-aware diff --check 通过（Git 有换行规范化提示）。构建日志 `tmp/three-fixes-*-build.log`、`tmp/three-fixes-production-build.log`。

| 产物 | 大小 bytes | SHA-256 |
| --- | ---: | --- |
| publish/windows/Hinge-Setup.exe | 198274238 | 30C1D1A37B98066985ECC0425C45D67825B578058D759CFA5FD90E4B93F8E09E |
| publish/windows/Hinge-Windows.zip | 129890683 | AA0D7AD097B12B48FFEC057FCCE8B912938DF8097718B64B0300BE1FCB0A7D04 |
| publish/Hinge.apk | 21159073 | B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3 |

ZIP 807 项完整性通过；包内 Hinge.dll 与正式输出字节一致，SHA-256 `D4E08E7997B9424CC146A9E0A1047A05D7D85830C1020F0D720CE7986A990DF5`；包含正常 Hinge.Main，不含临时验证键/样例/日志路径。EXE 内附 ZIP 与独立 ZIP 完全一致。安装器 Authenticode NotSigned，可能触发 SmartScreen；内部身份 MSIX 已按原构建流程签名。

按项目要求重建 ARM64 APK（Android 源码未改），apksigner v2/v3 验证通过。首次 Gradle 失败为 Java loopback 建连问题，加本次进程级 preferIPv4Stack 后成功；未改项目签名或包名，未暴露密钥。

## 尚未完成的验收

- 100%/150% Windows 系统 DPI：本轮尝试通过 computer-use 启动实际 SystemSettings.exe，但未得到 targetable window；没有改注册表或伪造缩放截图。125% 已实测，全 DPI 矩阵仍不完整。
- 激活/失活全部组合、caption Pressed 截帧、原生最小化/关闭到托盘和重启恢复状态没有在本轮逐项完整复测；对应业务代码保留。
- 真实设备日历同步/创建、文件拖入传输未实测；本轮未变更这些业务，不能把静态保留等同真实设备回归通过。
- 日历逐日键盘焦点全路径、所有配色组合未全覆盖；现有系统焦点视觉保留。
- 基线永久 200 DIP 空白未复现；已修复实测到的动画期旧拖柄和收起布局问题，若另有复现路径需要该窗口状态与缩放证据。

## 回退

`publish/rollback/pre-rail-caption-calendar-20261010/` 保存改前五份 source（含未改的 App.xaml.cs）、三个 packages 与开发文档。需要回退时，将 source 下 MainWindow.xaml/MainWindow.xaml.cs/CalendarPage.xaml/CalendarPage.xaml.cs 分别还原到 windows/Hinge.App；包可用 packages 中旧安装器/ZIP/APK。仅恢复本轮四个文件，避免 reset 整个已有脏工作树。App.xaml.cs 无需回退，因为已经字节恢复。保留该目录直到用户验收。

标题栏 API 依据：[Microsoft 自定义标题栏文档](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar?tabs=winui3)。NavigationView 模板/默认 Button 对齐依据本地实际还原的 Microsoft.WinUI generic.xaml，并用 WinApp CLI 项目 API 检索确认已有能力。

# Hinge Windows M3 三处定点修复验收报告（2026-10-10）

## 范围

仅处理关于应用、Android 无线 ADB 展开区、个性化页窗口材质菜单。沿用现有 WinUI 3 导航、M3 语义资源、ADB 事件和设置保存入口；没有改传输协议、BLE/ADB 连接实现、Android 业务或全局主题系统。

## 实现与实际检查

### 关于应用

- 用独立 AboutPage 替换旧 ContentDialog，通过现有 NavigationView.SelectionChanged 的 about 路由打开。保留应用图标、版本、产品/平台说明、功能介绍和 GitHub 项目链接。
- 在浅色和深色主题分别实际打开并截图；页面标题、版本、功能信息和链接均在可见区域，视觉上不再使用旧 Fluent 对话框及底部按钮区。
- 截图：visual-acceptance/20261010-three-ui/about-light-125.png、about-dark-125.png。

### Android 无线 ADB

- Expander 与外层设置分组使用 Stretch；状态行只在折叠标题中显示简短状态，展开后才显示详细 InfoBar。标题说明与状态放在内容区，展开箭头保持在控件最右侧。
- 实际截图确认折叠态显示“正在重连”；展开态主机、配对/连接端口、配对码、剪贴板同步开关、配对/连接/提取日志按钮和详细状态区均可见，字段和操作区没有挤出卡片。浅色/深色下错误状态分别使用匹配主题的柔和错误表面。
- 自动化检查了展开控件和 8 个核心控件；临时清空 UI 中的主机/端口用于隐藏本机值，截图后逐项恢复，未保存、配对、连接或提取日志。
- 本机当时没有找到已配对手机，截图中的后台剪贴板状态为重试失败；没有真机执行配对、连接、剪贴板传输或日志提取。因此这些真实设备操作不计为已验收。
- 截图：visual-acceptance/20261010-three-ui/adb-collapsed-light-125.png、adb-expanded-light-125.png、adb-collapsed-dark-125.png、adb-expanded-dark-125.png。

### 窗口材质菜单

- 在 PersonalizationPage 局部定义 ComboBoxItem 状态样式；四个材质标签直接复用 HingeM3BodyStyle（16 px 正文、常规字重），不重复设置字体尺寸。下拉面使用现有 M3 surface/onSurface、outline、hover/pressed、selected 和 focus 角色及 HingeM3ShapeMedium，覆盖 Light/Dark/HighContrast 资源字典。样式没有扩散到其他 ComboBox 或全局主题。
- 实际打开浅色、深色下拉菜单。125% 本机截图中，菜单文字清晰，字号和正文层级一致；圆角、背景及选中/键盘焦点颜色跟随当前主题。原选中的 MICA 材质没有切换。
- PersonalizationPage.xaml 未发现 RenderTransform 或 Composition 中间层声明；菜单字体取系统控件字体令牌。本机 125% 未复现文字模糊。其他 DPI/字体配置下的字体回退尚未验证。
- 截图：visual-acceptance/20261010-three-ui/material-dropdown-light-125.png、material-dropdown-dark-125.png。

## 构建、回归与环境边界

- 实际启动 Release 应用并截图。Windows 系统 DPI 为 120（96 DPI 基准下 125%）；测试时将应用窗口临时放大到 1800×1280 物理像素后恢复原窗口位置和尺寸。主题测试后恢复“跟随系统”，重新启动应用确认该设置仍被选中。
- Windows Release：dotnet build windows/Hinge.sln --configuration Release --no-restore -m:1 -nodeReuse:false，0 警告、0 错误；全量 Windows 测试 180/180 通过；目标源码 git diff --check 通过。
- 项目要求的 Android ARM64 Release APK 已构建并通过 APK 签名校验：com.hinge.office、1.4.14+119，SHA-256 B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3，与构建前一致；本次没有改 Android 源码。
- 当前环境只实际运行在 125% Windows 缩放；100% 和 150% 未验证，也未更改系统缩放。未逐个执行菜单 Hover/Pressed 状态及 Windows High Contrast 验收。

## 回退

修复前工作树源码和开发文档保存在 publish/rollback/pre-m3-three-ui-fixes-20261010/。其中 source/windows/Hinge.App/ 保存本次改动前的六个既有源码文件；MainWindow.xaml 的快照恢复了本次移除的 ItemInvoked 属性，并与当前文件相比只包含这一项差异。新增的 AboutPage.xaml 和 AboutPage.xaml.cs 删除即可随源码回退移除。构建前 APK 也保存于 publish/rollback/pre-m3-three-ui-fixes-20261010/android/Hinge.apk。


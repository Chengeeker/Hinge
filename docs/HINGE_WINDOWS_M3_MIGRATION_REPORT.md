# Hinge Windows M3 迁移报告（2026-10-10）

## 结论

WinUI 3 代码级主题迁移已完成，业务协议、传输实现和设置键未改；Release 构建、现有测试、Windows 候选包和按项目规程重建的 Android 主线 APK 均已生成。Android 源码未改，APK 与原有候选哈希相同。视觉验收仍待人工完成：规范所指的目标图 `screenshots/04_material3_settings_target.jpg` 不在提供的文件中，当前会话也没有 Windows 窗口截图能力，因此没有伪造截图或宣称已按图验收。

本轮没有安装候选包、推送或发布版本。候选仍为产品版本 `1.4.14.0`。

## 设计实现

### 主题资源

新增 `windows/Hinge.App/Themes/HingeM3.xaml`，提供 Light、Dark、HighContrast 三套主题资源、M3 语义颜色、形状/间距令牌、排版样式和按钮/输入框/列表控件样式。控件样式基于 WinUI 默认样式，保留其默认模板和键盘焦点视觉；微软文档建议优先用默认控件样式与 lightweight theme resources，而不是复制整套模板：[XAML styles](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-styles)。

默认色种子沿用 Hinge Android 的紫色 `#6750A4`。Light 令牌包括 `primary #6750A4`、`primaryContainer #EADDFF`、`surface #FEF7FF`、`surfaceContainerLow #F7F2FA`、`surfaceContainer #F3EDF7`、`onSurface #1D1B20`、`onSurfaceVariant #49454F`、`outline #79747E`、`outlineVariant #CAC4D0`、`error #B3261E`。Dark 令牌来自相同 M3 tonalSpot 策略生成并核对，包括 `primary #CFBDFE`、`primaryContainer #4D3D75`、`surface #141218`、`surfaceContainerLow #1D1B20`、`surfaceContainer #211F24`、`onSurface #E6E0E9`、`onSurfaceVariant #CAC4CF`、`outline #948F99`、`outlineVariant #49454E`、`error #FFB4AB`。Hinge 预设主题把 secondary/tertiary 角色映射到 primary 色族；HighContrast 使用系统窗口、文字和高亮颜色。

ListView/GridView 多选使用 M3 `primaryContainer` 作为选中背景、`onPrimaryContainer` 作为选中内容和勾选标记；选择指示条与 GridView 选中边框使用 M3 主色，悬停/按下选中态保持该配色，高对比度状态映射到 Windows 系统颜色。状态资源键按当前 Windows App SDK `generic.xaml` 中的实际键名覆盖，保留默认控件模板与焦点视觉。

Windows 个性化页目前只有 System/Light/Dark 选项，没有种子色选择器，因此本轮保留默认紫色色种子，没有新增主题色选择功能。页面最大宽度为 960 effective px，普通分组卡片半径 24，输入控件半径 12；设置行共享容器、细分隔线、14–17 px 文字层级和 4/8/12/16/24/32 间距令牌。

### 页面

- **P0 设置：** `SettingsPage` 按“外观与应用”“设备与服务”分为两个共享容器，组内入口行以低对比度分隔线隔开。`TransferSettingsPage` 将保存路径、设备连接和高级诊断拆成共享分组；配对码限制宽度 180，BLE 维持一次性 60 秒操作，ADB 参数在默认折叠的 Expander 中。所有既有控件名、AutomationId 和事件连接均保留。
- **P0 文件管理：** 移除分类和文件区域外的大卡片；设备/WebDAV 标签保留，标签高度回到 40，路径状态独立成行；主操作换成原生 CommandBar 并启用动态溢出，窄窗口时命令进入溢出菜单。筛选/排序仍使用原 ComboBox 和原处理逻辑。`BuildRemoteFileTile` 调整为 148 宽、最小高 144，媒体容器 124×86、图片 116×78、文件夹图标 38；标题 14 Regular，元信息 12。相册的 230×230 预览保持不变。
- **P1：** 主窗口保留 WinUI 原生 TitleBar、NavigationView、导航行为与 Footer；导航选中/悬停/按下颜色改为 M3 资源，普通项不设底色。首页、相册、通知页采用统一主题色和共享控件样式，照片缩略图动态背景也改为主题令牌。
- **P2：** 个性化、Windows 设置、Cloud Relay、WebDAV 设置、WebDAV 文件浏览、笔记、待办、日历和 Feature 页面应用共享语义令牌。WebDAV 多选色改为 M3 主题资源，并随系统颜色变化刷新。没有重写网络、文件或导航逻辑，也未增加依赖。

## 变更文件

18 个既有应用文件修改，新增 `windows/Hinge.App/Themes/HingeM3.xaml`：

`App.xaml`、`CloudRelaySettingsPage.xaml`、`FeaturePage.xaml`、`FileManagementPage.xaml`、`FileManagementPage.xaml.cs`、`HomePage.xaml`、`MainWindow.xaml`、`MainWindow.xaml.cs`、`NotificationHistoryPage.xaml`、`PersonalizationPage.xaml`、`PhotosPage.xaml`、`PhotosPage.xaml.cs`、`SettingsPage.xaml`、`ThemeBrushes.cs`、`TransferSettingsPage.xaml`、`WebDavBrowserView.cs`、`WebDavSettingsPage.xaml`、`WindowsSettingsPage.xaml`。

没有修改 Android 源码；仍按项目规程重建了 ARM64 主线签名 APK。该 APK 不包含 Windows UI，也不能作为 M3 窗口视觉验收。

## 构建与验证

- `dotnet build windows/Hinge.sln --configuration Release --no-restore -m:1 -nodeReuse:false`：成功，0 警告、0 错误。
- Windows 现有测试：180/180 通过，0 失败。测试使用项目内临时目录 `D:\App\Hinge\tmp\m3-test-temp`，以避开先前沙箱的系统 TEMP 写入限制。
- M3 资源自检：3 个 ThemeDictionary；应用 XAML 中 59 个不同 `HingeM3*` 引用均可解析；ListView/GridView 选中背景、悬停/按下态、选中文字、勾选标记、选择指示条和选中边框在 Light/Dark/HighContrast 三套字典中都有映射。
- `git -c safe.directory=D:/App/Hinge -c core.whitespace=cr-at-eol diff --check`：通过。Git 提醒若以后由 Git 改写，若干现有 CRLF 文件会转为 LF；这不是 whitespace 错误。
- 已构建本地 Windows 安装器和便携包；ZIP 807 项，包内 `Hinge.dll`、`Hinge.exe`、`Hinge.pri` 与发布输出哈希一致，主题字典由 XAML 编译并进入 PRI。安装器 Authenticode 状态为 `NotSigned`，适合作为本地验收候选，不是已签名的公开分发包。
- 按项目规程运行 `scripts/build_release_android.ps1` 重建主线 ARM64 APK：`com.hinge.office`、`1.4.14+119`、21,159,073 bytes；APK v2/v3 签名通过，稳定证书 SHA-256 为 `3eb0f6708904f8ef916c6a2572e394ba981ee25d64861344cf1921ca7ea17975`。新包哈希 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3` 与构建前 APK 相同；未安装到手机。
- 现有发布脚本的原始调用在此环境多次仅输出“正在确定要还原的项目…”后错误退出；同一 `dotnet publish` 单独运行返回 0。没有改动仓库中的发布脚本。我用脚本的临时副本增加发布退出码诊断，确认返回 0 后让原有完整封装流程成功；临时副本已删除，最终产物来自最后一次完整封装。

## 候选包

| 文件 | 版本/大小 | SHA-256 |
|---|---:|---|
| `publish/windows/Hinge-Setup.exe` | 1.4.14.0 / 198,257,764 bytes | `12CA1E1313B82A7065EE6AB26BA237329591FDE012947A6E69A5B2F29EF64BAC` |
| `publish/windows/Hinge-Windows.zip` | 129,874,209 bytes / 807 项 | `DFE46DA2F1ECFFB2FFF3BD1A0EE5D2281FAF59C242329DF7BC055CD789039943` |
| `publish/Hinge.apk` | 1.4.14+119 / 21,159,073 bytes / ARM64 | `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3` |

未安装候选，也未打开程序做截图验收。Light/Dark/HighContrast 的真实窗口显示、hover/focus、键盘、100%/125%/150% 缩放、窄窗口和文件操作仍需人工运行确认。

## 回退

修改前工作树干净，基线为 `860362396055cf49a4a0929ba5a51865530af3ee`。`.git` 在当前环境只读，未创建分支；已把完整基线源码和旧候选包存档在 `publish/rollback/pre-m3-migration-20261010/`：

- `Hinge-source-8603623.zip`（321 项）：SHA-256 `55B7E2FA57CC9309C3056169D607CAB5DC23D75427842CEED44F117EF2B326CA`。
- `Hinge-development-doc.md`：迁移前开发文档副本，SHA-256 `9FD03BA1EF41C304CB892A6BC0F2CA6CFF480DE0ED7C5FDDEE1FF8560B130422`。
- `windows/Hinge-Setup.exe`：修改前本地候选，SHA-256 `836C728064F4B5C7914574F5FCDACA3283345EB08313504304F21D92369DBD53`。
- `windows/Hinge-Windows.zip`：修改前本地候选，SHA-256 `966289BA0404F90B7ACB4318FC1EBD70EF1532FEB73E24E40F7F057611C91DF7`。
- `android/Hinge.apk`：覆盖前的主线 APK；SHA-256 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`，与重建产物一致。

拒收视觉时可直接使用 `publish/rollback/pre-m3-migration-20261010/windows/` 的修改前安装器恢复原界面；源码快照可解压到单独目录作为干净回退工作副本。当前候选未安装，因此现有安装实例未被替换。

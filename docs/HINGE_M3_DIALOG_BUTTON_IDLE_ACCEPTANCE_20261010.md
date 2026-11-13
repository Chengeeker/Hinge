# Hinge Windows M3 对话框主按钮默认态修复验收（2026-10-10）

> **已被后续验收报告取代：**该轮只验证了按钮配色，未修复默认态 Fluent Accent 按钮形状。请以 [`HINGE_M3_DIALOG_BUTTON_GEOMETRY_ACCEPTANCE_20261010.md`](HINGE_M3_DIALOG_BUTTON_GEOMETRY_ACCEPTANCE_20261010.md) 为准。

## 根因

日历“跳转到日期”对话框经 `MainWindow.ShowM3ContentDialogAsync` 进入共享 M3 样式 helper。helper 已设置 `PrimaryButtonStyle=HingeM3FilledButtonStyle`，但 WinUI 3 在 `DefaultButton=Primary` 时仍将主按钮按 Accent 按钮提供默认视觉；实际默认背景来自 `AccentButtonBackground` 等主题资源，所以屏幕上呈系统蓝色。WinUI 仓库记录了相同的 ContentDialog PrimaryButtonStyle 在默认按钮状态未加载的问题：[microsoft-ui-xaml #5035](https://github.com/microsoft/microsoft-ui-xaml/issues/5035)。

## 修复

仅修改 `windows/Hinge.App/MainWindow.xaml.cs` 的共享 `ApplyM3ContentDialogStyle`：在该对话框自己的 `Resources` 范围映射 Accent 按钮默认、悬停、按压、禁用背景及文字颜色到当前 M3 主色和 onPrimary 角色；悬停/按压按 8%/12% M3 state layer 混合。保留 PrimaryButtonStyle、CloseButtonStyle、DefaultButton、对话框门闩、导航/保存/取消语义；未修改全局主题字典、日历业务、Android 同步协议或其他功能。

## 实际验证

- Release 隔离候选（临时 AppInstance key；源文件构建后已按 SHA-256 `059AD97363528CE945D7A5179AA3AEB7BF72612A7057F590980816A592D79960` 恢复）实际运行，进入日历并打开“跳转到日期”。鼠标移离弹窗按钮时，未按下的“跳转”按钮显示当前 Sage M3 主色，不再显示 WinUI 默认蓝色。截图：[date-jump-primary-idle.png](visual-acceptance/20261010-dialog-button-m3/date-jump-primary-idle.png)。
- 实际点击“跳转”，弹窗关闭并返回日历；保留原选中日期和结果语义。原有按钮 `DefaultButton=Primary` 未改变。
- `dotnet test windows/Hinge.Tests/Hinge.Tests.csproj -c Release --no-restore -m:1 -nodeReuse:false`：180/180 通过。
- Windows Release 主线安装包构建成功。安装器 `publish/windows/Hinge-Setup.exe`：203,877,412 bytes，SHA-256 `93F41D7E507BDE038834E6416C556369F63301398096C4E6DB0D9F9DC56F7608`，Authenticode `NotSigned`。便携 ZIP：135,493,857 bytes，809 项，SHA-256 `B7DAE247C0FBC1F5F3305718A59B637E85E938F44878C994829CFDEAFB3B9509`；ZIP 内 `Hinge.dll` SHA-256 与本次 Release 输出一致：`A9250F33DD8FB2CBBE664DC30452227199DD2CB2F2E8997082F40BAD8E993DC5`。
- 按项目规程重建 Android ARM64 主线 APK（Android 源码未改）：`com.hinge.office` `1.4.14+119`，21,159,073 bytes，SHA-256 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`；APK v2/v3 签名校验通过。
- 目标增量 diff whitespace check 通过。构建日志：`tmp/dialog-button-final-build.log`、`tmp/dialog-button-windows-tests.log`、`tmp/dialog-button-windows-package.log`、`tmp/dialog-button-android-build.log`。

## 回退

回退快照：`publish/rollback/pre-20261010-dialog-button-m3/`，含改前 `MainWindow.xaml.cs`、日历文件、开发文档以及覆盖前 Windows 安装器/ZIP 和 Android APK。回退时只恢复目标源文件/产物，不要 reset 整个含既有改动的工作树。

## 未覆盖

只在本机当前浅色桌面环境实际检查了该日历弹窗默认按钮及跳转点击。深色/高对比度、100%/150% DPI、键盘默认按钮及其他共享 M3 弹窗未逐一做现场矩阵验收；候选安装器未安装，真实手机日历未连接。本地安装器没有 Authenticode 签名。

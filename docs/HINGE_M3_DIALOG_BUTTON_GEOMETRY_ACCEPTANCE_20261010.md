# Hinge Windows M3 对话框主按钮形状修复验收（2026-10-10）

## 修复结果

用户指出上一轮按钮只是改成 Sage 色，闲置时仍是 WinUI 3 默认按钮外观。该反馈成立。检查本机 Windows App SDK 2.3.6 的 `generic.xaml` 后确认：`ContentDialog` 的 `PrimaryAsDefaultButton` VisualState 通过静态资源 `{StaticResource AccentButtonStyle}` 替换 `PrimaryButtonStyle`，所以之前的 Accent 状态色映射无法改变实际模板尺寸和形状。

`MainWindow.ShowM3ContentDialogAsync()` 现在在显示 M3 弹窗前取消这个默认按钮视觉状态，让已存在的 `HingeM3FilledButtonStyle` 应用到主按钮。样式本身提供 38 DIP 最小高度、18 DIP 圆角和 16 DIP 水平 Padding。Enter 键由弹窗自身未处理的 KeyDown 处理并映射为 `ContentDialogResult.Primary`，因此调用方仍按原有 Primary 结果分支执行。取消按钮和日期跳转/新建日程业务代码没有改动。

本次实现只变更 `windows/Hinge.App/MainWindow.xaml.cs` 的 M3 弹窗共享入口；`App.xaml` 与改前快照的 SHA-256 一致（`0C45725EF11B7EFA35EB47C3F9A52597B5692DB60EF907737A4B0AB055712ACD`）。诊断过程中试过 App 根资源及 XamlControlsResources 同名资源覆盖，实际运行证明根资源覆盖不到模板的静态资源；将重复键放入 XamlControlsResources 会触发 XAML 重复键异常，这些方案都没有留在最终源码。

## 实际验证

- 以单独 AppInstance key 构建并运行 Release 候选，实际进入日历后打开“跳转到日期”。在未按压、鼠标离开按钮的截图中，“跳转”已显示 Sage 填充、M3 圆角和较高按钮尺寸，不再依赖长按/悬停。截图：[date-jump-primary-idle.jpg](visual-acceptance/20261010-dialog-button-m3-geometry/date-jump-primary-idle.jpg)。
- 实际点击“取消”后弹窗关闭并返回日历；再次打开后点击“跳转”也关闭弹窗并返回日历。弹窗打开时按 Enter 同样关闭，并由 helper 走 Primary 返回路径。没有创建或修改日历事件。
- 候选使用临时 AppInstance key `Hinge.Candidate.DialogGeometry`。构建后 `App.xaml.cs` 已按原始 SHA-256 恢复。用户原有 Debug 实例 PID 33000 保持运行，未被关闭。
- `dotnet test windows/Hinge.Tests/Hinge.Tests.csproj -c Release --no-restore -m:1 -nodeReuse:false`：180/180 通过。
- `scripts/build_release_windows.ps1` 最终完整构建成功。便携 ZIP 809 项逐项完整读取；内含 `Hinge.dll` 与 Release 输出一致，SHA-256 `9FDFA728C6C301AF0C35352D3F6409BDCFD8058E3730954FFE0448EA7848ECF6`。ZIP SHA-256 `D155AD87EAFA98D4EEF01DEA92284438EB4305C6789830CA02A788933C9D1E05`，大小 135,493,948 bytes。安装器 `publish/windows/Hinge-Setup.exe` SHA-256 `C09C498B56D5D13E8E428AF060C5F66E652AF80B4469F13B484388BC29483ACD`，大小 203,877,503 bytes；Authenticode 状态为 `NotSigned`。正式构建日志：`tmp/dialog-windows-release.log`。
- 按 Hinge 项目要求重建 Android 主线 APK（Android 源码未改）：`com.hinge.office`、`1.4.14+119`，21,159,073 bytes，SHA-256 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`；APK v2/v3 签名验证通过。
- `git diff --check` 无空白错误；仓库对既有 CRLF 文件仍输出行尾转换提示。

## 尚未覆盖

实际视觉验收在本机浅色主题和当前 125% 缩放环境完成。深色/高对比度、100%/150% DPI、其他 M3 弹窗以及不同键盘输入控件组合未逐项实测；没有连接手机做同步回归。安装器没有 Authenticode 签名。

## 回退

改前快照位于 `publish/rollback/pre-20261010-dialog-button-geometry/`，包括本次触碰的源文件、开发文档、原 Windows 安装器/ZIP 和原 Android APK。回退时只恢复快照中的对应文件和产物，不要重置整个工作树。
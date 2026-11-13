# Hinge Windows 机型品牌标识与通知列表间距修复验收（2026-10-10）

## 本次改动

- `windows/Hinge.App/FileManagementPage.xaml.cs`：手机 Tab 头部的品牌 SVG 从 26×20 DIP 调整到与首页历史记录相同的 34×24 DIP；固定总宽 160 DIP，名称区域相应由 126 缩至 118 DIP，间距保持 8 DIP。复用原 `BuildOfficialBrandLogo`、SVG 和品牌识别，不改 Tab 路由及设备选择。
- `windows/Hinge.App/PersonalizationPage.xaml`：配色选项可见文字及 Automation Name 从“鼠尾草绿（参考图）”改为“鼠尾草绿”；Tag、色值预览和配色应用逻辑未改。
- `windows/Hinge.App/NotificationHistoryPage.xaml`、`.xaml.cs`：空白由固定 32 DIP 高、闲置时仍参与布局的 ProgressRing 造成。默认折叠，加载期间显示，`finally` 中停止并折叠，因此非加载状态不再为它预留空间。

## 验证

- Windows Release 安装器与 ZIP：`scripts/build_release_windows.ps1` 成功，MSBuild 0 warnings / 0 errors。ZIP 809 项完整读取无损坏，包含 6 个品牌 SVG；ZIP 内 `Hinge.dll` SHA-256 与 Release 输出一致：`D250FB3AB923A3F6F5542B91D56433EB436A8D52A87148C865B0FC12141FAB94`。
- Windows Tests：180/180 通过。
- Android 主线 APK 按仓库约定重建；Android 源码未修改。APK v2/v3 签名验证通过，SHA-256：`B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`。
- 安装器 `publish/windows/Hinge-Setup.exe`：203,877,305 bytes，SHA-256 `0EABA7694B3686722B629D4F966DF9101E11E3FFE94D1AE8F4FCE7821A2D97DA`。
- 便携包 `publish/windows/Hinge-Windows.zip`：135,493,750 bytes，SHA-256 `B3A578DD2D4D446C7668B560071D7282927EBD5D8547AA3FFBF7A7983A678E5D`。
- 代码级尺寸与可见文案已核对。图形自动化当前未枚举到 Hinge 窗口，因此未对改后应用截取真实界面，也未实际比较 Logo 清晰度或通知首行位置；对应视觉效果仍需在启动本轮安装包后确认。构建不是界面验收。
- 安装器 Authenticode 状态为 `NotSigned`。

## 回退

`publish/rollback/pre-file-management-brand-logo-personalization-label-20261010/` 保存了本轮改前的 Windows 安装器、Windows ZIP、APK，以及四份改动前源文件和开发文档。回退时只恢复 `FileManagementPage.xaml.cs`、`PersonalizationPage.xaml`、`NotificationHistoryPage.xaml`、`NotificationHistoryPage.xaml.cs` 中本轮修改和需要回退的交付包，不要重置整个工作树。


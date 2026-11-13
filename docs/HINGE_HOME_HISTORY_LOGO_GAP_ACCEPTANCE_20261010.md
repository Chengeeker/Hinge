# Hinge Windows 首页传输记录 Logo 间距补充验收（2026-10-10）

## 修改

只修改 `windows/Hinge.App/HomePage.xaml.cs` 的 `SetTransferHistory()`：Logo 列从 44 DIP 调为 56 DIP，并为 Logo 使用与附近设备相同的 56×56 居中槽；详情列左/右边距设为 12/8 DIP。历史行高、文字样式、操作按钮、事件绑定及滚动均未改。

## 验证

- 隔离 Release 候选真实运行首页并截图。Logo 与文件名/状态文字间距已拉开，四列历史项目仍整齐排列，右侧删除按钮保持原位。截图：`visual-acceptance/20261010-home-history-logo-gap/home-transfer-history-logo-gap.png`。
- Windows Release 主线安装器/ZIP构建成功；ZIP 809 项均完整读取，包内 `Hinge.dll` 与 Release 输出 SHA-256 一致：`071F178355FF8B617D25CED8D0B64AA6D28E2DB8061769CC71B9868F89CF5580`。
- Windows 测试 180/180 通过，`HomePage.xaml.cs` 的 `git diff --check` 通过。
- Android 源码未变，仍按项目规程重建 Release APK；哈希 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`。
- 当前截图窗口使用 125% DPI；没有为这次间距调整重测 100%/150%。没有点击删除按钮或清空记录。

## 包和回退

- `publish/windows/Hinge-Setup.exe`：203,877,094 bytes，SHA-256 `35088330C2C074059A6D0761210C8FF08B2A181A99749F09D28D2487830A3781`。
- `publish/windows/Hinge-Windows.zip`：135,493,539 bytes，SHA-256 `9E9B4500AD5C34958B07EA07AAB602556D422A64E750F3604F338D277A65259D`。
- 回退目录 `publish/rollback/pre-home-history-logo-gap-20261010/` 保存此轮改前 `HomePage.xaml.cs`、开发文档和 Windows/APK 包。只恢复本轮目标文件与交付包，不要 reset 整个工作树。安装器 Authenticode 为 NotSigned；未安装、提交或推送。
# Windows 未连接状态设备 Logo 修复（2026-10-11）

## 改动

首页在 `SetHeroDevice(null)` 时原本直接显示 `Symbol.Phone`，因此未连接状态绕过了品牌 Logo 选择器。现将此占位状态改为调用既有 `BuildOfficialBrandLogo`，以本地 `android-default.jpg` 替代电话符号；识别到品牌时的 SVG 映射和其他设备行逻辑不变。仅改 `windows/Hinge.App/MainWindow.xaml.cs` 一处，不改 Android、协议或连接状态。

## 验证

- Windows Release 安装器与便携 ZIP 构建成功。ZIP 共 810 个条目，均可完整读取；其中 JPG SHA-256 与资源相同：`E54130FCA367A89A4EC120FE92C1AB86410AAD682E6EBC5C96D8572E9EAF1250`，`Hinge.dll` 与 Release 输出一致，SHA-256 `9D88D72A6C5D3C6BEB32562445B88BCB7C5F151A1E24E1563BD27F8E730B2180`。
- Windows ZIP：135,523,959 bytes，SHA-256 `DC5233FBAC02158D96B286CB9AEFF3609060E07D9D11B4BAE139FA511A425C55`。安装器：203,907,776 bytes，SHA-256 `E608ECED40014C03167CA45D6F51A2D3C60902610969FB9DC66172F2D8B55742`；内嵌 ZIP 与独立 ZIP 的长度及 SHA-256 一致。安装器外层未签名。
- 按项目要求重建 Android 主线 APK：`com.hinge.office` `1.4.15+120`，21,163,270 bytes，SHA-256 `7976C4FD921A7A070DCDE81190B109EE7389BFF42D54216823FB964159B21001`；APK v2/v3 签名通过，包内默认图条目仍存在。Android 源码未改。
- 仅完成代码构建和包内资源/完整性验证。没有启动 Windows GUI 做未连接首页截图验收，也没有实际安装新包。

## 回退

修改前 `MainWindow.xaml.cs` 以及当时的 APK、安装器和 ZIP 保存在 `publish/rollback/pre-disconnected-default-logo-20261011/`。回退只需恢复该 C# 文件；如需恢复旧分发包，按需恢复快照中对应的单个包，不要覆盖整个工作树。

# Hinge v2.0.0

本版本按产品里程碑将 Hinge 应用版本升级至 2.0.0，并修复 Android 端对 Windows 主动连接状态显示滞后的问题。已有 Material 3、日历、个性化字体、Windows 布局及 Android 无线 ADB 更新详见 [v1.4.15 更新日志](CHANGELOG_v1.4.15.md)。
跨端传输协议版本保持 `0.1`，本次是应用版本升级，不代表协议有破坏性变更。Android `versionCode` 从 120 增至 121，包名和签名保持不变，以便从 v1.4.15 覆盖更新。

## 版本

- Android：`2.0.0+121`，`com.hinge.office`，`arm64-v8a`。
- Windows：`2.0.0.0`。

## 修复

- Android：Windows 端主动建立或恢复会话后，Android 设备页现在会订阅新会话的连接状态和对端信息，避免电脑已显示连接、手机端仍停留在“已发现”状态。传输协议、设备发现和会话建立流程未改。
- 新增会话在应用启动后创建的 UI 回归测试；这验证会话状态呈现路径，不替代真实手机与 Windows 设备的现场验收。

## 构建和资产验证

Android Flutter 测试 `77/77` 通过，其中定向设备页测试 `12/12`；Android Release APK 和 Windows Release 安装器/便携包构建完成。APK 的版本/签名信息由发布脚本验证，便携 ZIP 可完整读取，包内 `Hinge.dll` 与 Windows Release 输出哈希一致。没有在真实 Android 手机上复现原故障或现场验收修复效果。

| 平台 | 资产 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| Android | `Hinge.apk` | 21,163,270 bytes | `CE99D9E66DA9B1F5106E5B7845CA0FF43CC57284C4C934989DBBF2EC037018EF` |
| Windows | `Hinge-Setup.exe` | 203,924,318 bytes | `D832697EC1A22D292E88DC97AFE472187DAD43BAF4B14C45742F5067FC788155` |
| Windows | `Hinge-Windows.zip` | 135,540,519 bytes | `29F4E220D051AC851D873E7C33BB6D263AD6D3AAEB49F51CA8EF6AA86C626525` |

Windows 安装器 FileVersion 为 `2.0.0.0`；便携 ZIP 共 810 项，内嵌 `Hinge.dll` SHA-256 为 `B2286DD39E06B96D2D8CC5197EB901B5F08E1399DFD307B80E9C3379CCE21982`，与 Release 输出一致。安装器外层 Authenticode 为 `NotSigned`，首次运行时 Windows 可能提示来源。

回退用的 v1.4.15 安装器、便携 ZIP 与 APK 保存在 `publish/rollback/pre-release-v2.0.0-20261011/`，备份哈希记录在该目录的 `SHA256SUMS.txt`。

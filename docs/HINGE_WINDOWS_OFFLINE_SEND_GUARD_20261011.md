# Windows 首页离线发送保护验收（2026-10-11）

## 修复范围

首页“发送文件”按钮仅在存在已就绪且状态为 `Connected` 的会话时启用。发送处理入口会检查连接；文件选择器关闭后再次检查，避免用户打开选择器期间连接断开仍继续发送。离线时显示“无法发送文件 / 请先连接设备。”

首页这条发送入口不再回退到已信任的 Cloud Relay 设备。其它 Cloud Relay、命令路由和传输流程未改。

## 验证

- Windows Release 发布脚本构建成功。
- 安装器载荷标记为 `HINGE_PAYLOAD_V1`，载荷长度 135,523,927 bytes；从安装器中提取载荷计算的 SHA-256 与便携 ZIP 一致：`7AF56AD1EAA27D5CCD1564FA77BE5B744A767AD78B8F87975CC70A64A87E4B44`。
- Windows 安装器：203,907,744 bytes，SHA-256 `3D68DF32ADC547F3D94C167AFA1684825B899FFAD4927A146E493E290CF4EED2`；Authenticode 状态 `NotSigned`。
- Windows 便携 ZIP：135,523,927 bytes，810 项可读；SHA-256 `7AF56AD1EAA27D5CCD1564FA77BE5B744A767AD78B8F87975CC70A64A87E4B44`。
- 按项目规则重建 Android 主线 APK（Android 源码未改）：`com.hinge.office` `1.4.15+120`，21,163,270 bytes，SHA-256 `7976C4FD921A7A070DCDE81190B109EE7389BFF42D54216823FB964159B21001`；APK v2/v3 签名通过。
- 仅检查了构建和交付包完整性；没有安装候选版、运行 Windows GUI 或执行真实设备传输，因此未验证离线禁用按钮的运行时视觉/点击行为，也未运行测试。

## 回退

改前源码和 Windows 安装器、Windows ZIP、Android APK 已保存于 `publish/rollback/pre-offline-send-disabled-20261011/`。按需恢复说明见该目录的 `ROLLBACK.md`。回退时只恢复 `MainWindow.xaml.cs` 或相应旧交付包，不要覆盖整个工作树。

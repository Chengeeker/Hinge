# Hinge v1.0.27-dev.1

这是 Hinge 的双端开发版，安装包运行版本为 Android `1.0.27+28`、Windows `1.0.27.0`。

## 本版内容

- Android 增加可选短信/彩信同步，明确授权后监听新消息并通过可信会话转发到 Windows；
- Windows 短信/彩信使用托盘气泡，识别为验证码时点击整条气泡复制，普通通知不会复制；
- 保留 ARM64 APK、稳定 Android 签名、自包含 EXE 安装器和便携 ZIP 的发布约定；
- 同步开发文档、协议说明和当前版本的真实设备验收边界。

## 验证

- Android Flutter tests：53 项通过；
- Windows .NET tests：63 项通过；
- Android APK 包名、ARM64 架构、V2/V3 签名和 Windows 文件版本均已校验；
- Windows 发布资产仅包含 EXE 安装器和便携 ZIP，不提供 MSIX。

## 已知边界

这是开发版。传统托盘气泡不支持嵌入式按钮，验证码采用点击整条气泡复制；不同 Android 厂商短信权限、后台策略、弱网、多网卡和 Windows 防火墙场景仍需真实设备验收。

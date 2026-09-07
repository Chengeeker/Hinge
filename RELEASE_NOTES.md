# Hinge v1.0.17-dev.1

这是 Hinge 的双端开发版，安装包运行版本为 Android `1.0.17+18`、Windows `1.0.17.0`。

## 本版内容

- Android 与 Windows 版本号统一推进，保留稳定 Android 签名以支持覆盖更新；
- Windows 文件管理、相册的分页加载、缩略图缓存、多选保存和自定义接收目录继续按当前实现打包；
- Android Monet 动态配色、品牌资源、默认应用、保活设置和紧凑悬浮底栏随当前源码发布；
- 补齐开发文档、构建验证、发布资产和真实设备验收边界说明。

## 验证

- Android Flutter tests：49 项通过；
- Windows .NET tests：59 项通过；
- Android APK 签名、Windows EXE 安装器、便携 ZIP 和可用 MSIX 已重新生成。

## 已知边界

这是开发版。投屏、通知回复、OCR、完整端到端加密握手，以及不同 Android 厂商后台策略仍不应视为已完成的生产能力；大型媒体库、弱网、多网卡和 Windows 防火墙场景仍需真实设备验收。

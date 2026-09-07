# Hinge v1.0.0

这是 Hinge 的首个公开版本，面向 Android 与 Windows 的局域网跨设备工作流。发布资产和安装说明见 [GitHub Release v1.0.0](https://github.com/Chengeeker/Hinge/releases/tag/v1.0.0)。

## 交付内容

- Android ARM64 Release APK；
- Windows WinUI 3 自包含 EXE 安装器，可选择安装目录；
- Windows 便携 ZIP；
- 可选 MSIX 和对应测试证书。

## 主要能力

- 局域网设备发现、会话、文件传输和剪贴板同步；
- Windows 文件管理、相册、笔记、待办、日历、工具和设置；
- Android 工作区、Monet 动态取色、预置主题和保活设置；
- 文件与相册分页加载、缩略图、筛选、排序和目录导航；
- Windows 系统默认应用打开图片、视频和音频；
- Windows 文件拖入目标预览，以及应用内文件拖出取消发送；
- Android 与 Windows 源码、资源、文档、安装脚本和交付文件统一使用 Hinge 品牌。

## 已知限制

- 手机投屏、真实通知回复和 OCR 尚未作为本版本的完成能力交付；
- Android 厂商省电/锁屏策略可能仍会回收连接；
- 大型媒体库、文档目录、弱网、多网卡和不同 Windows 权限级别需要继续进行真实设备验收。

## 验证基线

- Flutter 静态分析与测试；
- Windows Release 编译与单元测试；
- Android APK 签名验证；
- Windows EXE 安装器和便携包构建验证。

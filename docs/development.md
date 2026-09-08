# Hinge 开发文档

本文是 Hinge 日常开发、验证和交付的入口，内容以当前仓库实现为准。历史审计和规划文档可以补充背景，但不能替代源码、协议和实际构建结果。

## 1. 项目边界

Hinge 是一个局域网优先的 Android + Windows 跨设备工作台。当前工程由三个相互配合的部分组成：

- `android/`：Flutter 页面、工作区和设置，以及 Android 原生 MethodChannel、前台服务、MediaStore、CalendarContract、通知和系统设置跳转；
- `windows/`：WinUI 3 / Windows App SDK 桌面客户端、文件管理、相册、设置、托盘和 Windows 平台适配；
- `protocol/`：发现、会话、传输、剪贴板、通知、配对和实验性投屏的跨端帧与状态说明。

安装器和发布脚本位于 `installer/`、`scripts/`；发布目录 `publish/` 与临时目录 `tmp/` 只用于本地构建，均不进入 Git。

## 2. 连接与数据流

1. 两端通过 UDP `52830` 广播或定向探测发现设备；
2. 通过 TCP `52831` 建立会话，完成设备身份和能力状态同步；
3. `DeviceRegistry` 维护已发现、已连接和历史设备记录，`SessionManager` 提供当前有效会话；
4. 文件、相册、工作区和剪贴板请求都经过当前会话，不由单个页面私自创建 Socket；
5. Android 前台服务、Wi-Fi/组播锁和 Windows 托盘用于降低后台断连概率，但不能绕过 Android 厂商电池策略或 Windows UAC/防火墙规则。

协议字段变更必须同时更新两端实现、`protocol/` 文档和测试向量。协议端口、帧边界和传输约束以 `protocol/protocol.md`、`session.md`、`transfer.md` 为准。

## 3. 文件和相册实现约定

- 首次列表请求只取前 200 项，同时保留服务端返回的总数；滚动接近末尾时再请求下一页；
- 缩略图和预览走独立缓存，页面切换或请求取消后不能把旧结果写回当前页面；
- Windows 双击图片、视频、音频时使用系统默认文件关联；远程预览文件放在临时缓存，不把预览误写入用户接收目录；
- 多选保存必须串行化或按传输 ID 隔离，禁止多个接收任务共享同一个临时文件；
- 外部文件拖入只有在文件管理/相册的内容区才显示目标提示，侧边栏和窗口移动不属于投放区域；
- 自定义接收目录保存在用户设置中，更新安装包不能回退到默认目录；
- 目录、微信、QQ 和最近文件的过滤不能用“文件小于某个大小”作为唯一条件，应先依据目录项类型、路径和缓存命名判断。

## 4. 开发环境

### Android

- Flutter SDK：`D:\flutter_sdk\bin\flutter.bat`；
- JDK 17；
- Android SDK、对应 build-tools 和 Bouncy Castle provider；
- 发布签名库只从本机路径读取，禁止提交到 Git。

### Windows

- .NET 8 SDK；
- Windows 10/11 SDK；
- Visual Studio 的 C++ 桌面开发工具；
- Windows App SDK / WinUI 3 构建环境。

## 5. 日常验证

```powershell
cd android
flutter pub get
flutter analyze --no-pub
flutter test --no-pub

dotnet test windows/Hinge.sln --no-restore
git diff --check
```

通过测试不等于完成真机验收。至少还要覆盖：同一 Wi-Fi 的 UDP/TCP 发现、多个网卡、防火墙、锁屏/切后台、Wi-Fi 切换、大型媒体库、多文件传输和重复文件名。

## 6. 构建开发版

当前开发版产品版本为 `1.0.17`，GitHub 标签为 `v1.0.17-dev.1`。Android build number 为 `18`，Windows AppX 版本为 `1.0.17.0`。

```powershell
$env:HINGE_KEYSTORE_PASSWORD = '<keystore-password>'
$env:HINGE_KEY_PASSWORD = '<key-password>'
$env:HINGE_KEYSTORE_PATH = '<path-to-your-keystore.bks>'
$env:HINGE_KEY_ALIAS = '<your-key-alias>'
.\scripts\build_release_android.ps1
.\scripts\build_release_windows.ps1
```

构建产物：

- `publish/android/Hinge.apk`；
- `publish/windows/Hinge-Setup.exe`；
- `publish/windows/Hinge-Windows.zip`；
- 可用时还有 `publish/windows/Hinge-Installer.msix` 和 `Hinge-Installer.cer`。

发布前检查 Android APK 的 V2/V3 签名、包名 `com.hinge.office`、版本名/构建号，以及 AppX 清单版本。不要把密码、签名库、`publish/` 或 `tmp/` 加入 Git。

## 7. 版本与 GitHub 发布

1. 同步 `android/pubspec.yaml`、`android/lib/core/constants.dart`、`windows/Hinge.Core/Constants.cs`、两个 Windows manifest 和 `CHANGELOG.md`；
2. 运行双端测试和构建，计算发布资产 SHA-256；
3. 提交源码、协议和文档到 `main`；
4. 推送标签并创建 GitHub pre-release，上传 APK、EXE、ZIP，以及确实生成的 MSIX/CER；
5. 发布说明只写已经验证的内容，并明确真实设备仍需验收的边界。

## 8. 目前不能过度宣传的能力

手机投屏、真实通知回复、OCR、完整的端到端加密握手和所有 Android 厂商后台策略尚未形成“所有环境可用”的生产闭环。Mock、localhost 回环测试和单元测试只能证明局部逻辑，不能替代双端真实设备测试。

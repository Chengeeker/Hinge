# Hinge 开发文档

本文是 Hinge 日常开发、验证和交付的入口，内容以当前仓库实现为准。历史审计和规划文档可以补充背景，但不能替代源码、协议和实际构建结果。

## 1. 项目边界

Hinge 是一个局域网优先的 Android + Windows 跨设备工作台。当前工程由三个相互配合的部分组成：

- `android/`：Flutter 页面、工作区和设置，以及 Android 原生 MethodChannel、EventChannel、前台服务、MediaStore、CalendarContract、短信广播、通知历史监听和系统设置跳转；
- `windows/`：WinUI 3 / Windows App SDK 桌面客户端、文件管理、相册、设置、托盘和 Windows 平台适配；
- `protocol/`：发现、会话、传输、剪贴板、通知、配对和实验性投屏的跨端帧与状态说明。

安装器和发布脚本位于 `installer/`、`scripts/`；发布目录 `publish/` 与临时目录 `tmp/` 只用于本地构建，均不进入 Git。

## 1.1 每次操作前的文档检查

任何源码修改、构建、签名、压缩、复制安装包或发布操作前，先完整审阅本文和 `docs/release.md`，确认当前版本、输出路径、签名规则和验证门槛；操作完成后再把实际结果同步回开发文档。文档要求与脚本实现不一致时，以修正后的脚本和本次验证结果为准，并立即修正文档。

## 2. 连接与数据流

1. 两端通过 UDP `52830` 广播或定向探测发现设备；
2. 通过 TCP `52831` 建立会话，完成设备身份和能力状态同步；Android 在创建 socket 前优先绑定当前 Wi‑Fi 网络；
3. `DeviceRegistry` 维护已发现、已连接和历史设备记录，`SessionManager` 提供当前有效会话；
4. 文件、相册、工作区和剪贴板请求都经过当前会话，不由单个页面私自创建 Socket；
5. Android 前台服务、Wi-Fi/组播锁和 Windows 托盘用于降低后台断连概率；发现报文带有可选 `discoveryPort`，固定 UDP 端口被占用时允许对端回到实际临时端口；两端用持久化的 `deviceId` 和信任记录定义“历史设备”，发现到已信任设备后自动尝试会话恢复，自动反向请求带有 `automaticReconnect=true`，接收端只对本地已信任的相同身份放行；自动尝试失败后每 5 秒重试一次，新设备仍需用户手动操作。这些机制不能绕过 Android 厂商电池策略或 Windows UAC/防火墙规则。

协议字段变更必须同时更新两端实现、`protocol/` 文档和测试向量。协议端口、帧边界和传输约束以 `protocol/protocol.md`、`session.md`、`transfer.md` 为准。

## 3. 文件和相册实现约定

- 首次列表请求只取前 200 项，同时保留服务端返回的总数；滚动接近末尾时再请求下一页；
- 缩略图和预览走独立缓存，页面切换或请求取消后不能把旧结果写回当前页面；
- Windows 双击图片、视频、音频时使用系统默认文件关联；远程预览文件放在临时缓存，不把预览误写入用户接收目录；
- 多选保存必须串行化或按传输 ID 隔离，禁止多个接收任务共享同一个临时文件；
- 外部文件拖入只有在文件管理/相册的内容区才显示目标提示，侧边栏和窗口移动不属于投放区域；
- 自定义接收目录保存在用户设置中，更新安装包不能回退到默认目录；
- 目录、微信、QQ 和最近文件的过滤不能用“文件小于某个大小”作为唯一条件，应先依据目录项类型、路径和缓存命名判断。

## 4. 通知历史约定

- Android 的 `SmsNotificationListenerService` 同时承载短信兼容转发和通用通知历史，两者使用独立开关；
- 通知历史必须先通过系统“通知访问”授权，再由用户在“工作区 > 通知历史”开启采集；正文、包名、时间、应用名称和通知 key 写入应用私有 SQLite，不写共享存储和日志；
- Windows 通过 `notificationHistory` 工作区命令按页读取，首屏最多 100 条，继续滚动才请求后续记录；默认最早在上，支持倒序和包名筛选；
- Windows 点击微信/QQ通知时只负责唤醒桌面客户端：先检查当前 `Weixin.exe`/`WeChat.exe`/`QQ.exe`/`QQNT.exe` 进程，只对已找到的主窗口执行必要的恢复和置前，不枚举或操作子窗口，不发送 `mqq://`、`weixin://` 深链，也不回传 Android `PendingIntent`，避免 QQ 多进程渲染窗口被错误操作而卡死。没有现有进程时才启动已安装的可执行文件；找不到客户端时保持无操作，不弹出 Windows 的“获取打开此链接的应用”对话框；
- `notificationHistoryAction` 使用 `action=open`、`action=delete` 和 `action=clear` 三种动作；Windows 和 Android 通知历史页都支持单条删除，顶部支持确认后清空，删除后必须重新读取总数、分页和应用筛选统计；
- 新增或修改通知历史字段时，必须同步修改 `NotificationHistoryStore`、`notification_history_model.dart`、`WorkspaceRemoteClient.cs`、Windows 页面和 `protocol/notification.md`。

## 5. 开发环境

### Android

- Flutter SDK：`D:\flutter_sdk\bin\flutter.bat`；
- JDK 17；
- Android SDK、对应 build-tools 和 Bouncy Castle provider；
- 发布签名库只从本机路径读取，禁止提交到 Git。当前开发机的固定签名库路径为 `D:\Download\backup\infinitycm.bks`；构建脚本检测到该文件时优先使用它，其他机器可通过 `HINGE_KEYSTORE_PATH` 指定自己的签名库。
- 发布脚本使用 Review 项目采用的本地 `key.properties` 约定：首次复制 `android/android/key.properties.example` 为 `android/android/key.properties` 并填写本机签名配置，后续构建自动读取，不再重复弹出输入窗口。该文件已被 Git 忽略，不能提交；其他机器应配置自己的签名文件。CI 或临时构建仍可通过进程环境变量/参数覆盖本地配置。
- BKS 签名库不能直接交给 `apksigner`，脚本会在项目临时目录之外创建临时 PKCS12 副本供签名，完成后清理；密码不会打印到日志、测试输出或发行版。
- Android 发布脚本优先使用已验证的 `D:\jdk17`，并为 Gradle 设置项目内 `tmp/` 临时目录和无守护进程模式；不要让系统 Java 26 shim 覆盖 `JAVA_HOME`。

### Windows

- .NET 8 SDK；
- Windows 10/11 SDK；
- Visual Studio 的 C++ 桌面开发工具；
- Windows App SDK / WinUI 3 构建环境。

## 6. 日常验证

```powershell
cd android
flutter pub get
flutter analyze --no-pub
flutter test --no-pub

dotnet test windows/Hinge.sln --no-restore
git diff --check
```

通过测试不等于完成真机验收。至少还要覆盖：同一 Wi-Fi 的 UDP/TCP 发现、多个网卡、防火墙、锁屏/切后台、Wi-Fi 切换、大型媒体库、多文件传输和重复文件名。

## 7. 构建发布版

当前仓库对应的历史开发版为 `1.0.32`，GitHub 标签为 `v1.0.32-dev.1`；Android build number 为 `33`，Windows 文件版本为 `1.0.32.0`。后续可交付版本直接使用稳定版本号（例如 `1.0.33`），GitHub Release 直接标记为 `Latest`，不再使用 `-dev`、`-pre` 后缀或 Pre-release 标签。

```powershell
Copy-Item android/android/key.properties.example android/android/key.properties
# 只需第一次编辑 android/android/key.properties，填写本机签名信息
.\scripts\build_release_android.ps1
.\scripts\build_release_windows.ps1
```

构建产物：

- `publish/Hinge.apk`；
- `publish/windows/Hinge-Setup.exe`；
- `publish/windows/Hinge-Windows.zip`；
- Android 只交付 `publish/Hinge.apk`。`android/build/app/outputs/flutter-apk/app-release.apk` 是 Flutter 中间产物，不得直接作为更新包分发；脚本始终使用 `android-arm64` 构建、重新使用稳定签名并在复制前校验 `arm64-v8a`、包名、版本和签名。
- Android 签名或校验失败时不得覆盖 `publish/Hinge.apk`，避免把调试签名或其他签名的 APK 当成更新包。
- 新的签名 APK 成功复制到 `publish/Hinge.apk` 后，脚本才会清理已知的旧路径 `publish/android/Hinge.apk`；在新包生成前不会删除旧包。
- Windows 发布只包含 `publish/windows/Hinge-Setup.exe` 和 `publish/windows/Hinge-Windows.zip`，不生成或上传 MSIX / 测试证书。
- EXE 安装器会给开始菜单快捷方式写入稳定的 `Hinge.Office` AppUserModelId；这是 unpackaged EXE 使用 Windows 原生 Toast 的必要身份信息。短信通知正常路径只显示在 Windows 通知中心，应用内卡片仅作为系统通知不可用时的诊断兜底。
- 短信/彩信通知固定使用 `Win32TrayManager` 的 `NotifyIcon.ShowBalloonTip` 托盘气泡；只有识别为验证码时才绑定点击复制动作，普通通知点击仍打开 Hinge，不再显示右上角应用内浮层。
- 压缩规则：Windows 单文件 EXE 和便携 ZIP 由 Windows 发布脚本处理；APK 使用 Android/Flutter 构建流程的压缩与裁剪，签名完成后不得再次手工解压、重打包或压缩 APK，否则可能破坏 APK 签名。任何自动压缩步骤都必须保留在脚本中并记录在本文和 `docs/release.md`。

发布前检查 Android APK 的 V2/V3 签名、包名 `com.hinge.office`、版本名/构建号，以及 Windows 文件版本。不要把密码、签名库、`publish/` 或 `tmp/` 加入 Git。

## 8. 版本与 GitHub 发布

1. 同步 `android/pubspec.yaml`、`android/lib/core/constants.dart`、`windows/Hinge.Core/Constants.cs`、两个 Windows manifest 和 `CHANGELOG.md`；
2. 运行双端测试和构建，计算发布资产 SHA-256；
3. 提交源码、协议和文档到 `main`；
4. 后续稳定版本推送版本标签并创建 GitHub Release，直接标记为 `Latest`，上传 APK、EXE 和 ZIP；只有明确需要保留测试快照时才使用历史开发版的 Pre-release 形式；
5. 发布说明只写已经验证的内容，并明确真实设备仍需验收的边界。

## 9. 目前不能过度宣传的能力

手机投屏、真实通知回复、OCR、完整的端到端加密握手和所有 Android 厂商后台策略尚未形成“所有环境可用”的生产闭环。Mock、localhost 回环测试和单元测试只能证明局部逻辑，不能替代双端真实设备测试。

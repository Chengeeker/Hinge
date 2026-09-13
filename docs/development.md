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
- Windows 11 第一层“通过 Hinge 发送到”由静态链接运行库的 `Hinge.ShellExtension.dll` 原生 `IExplorerCommand` 提供，根命令返回 `ECF_HASSUBCOMMANDS`，悬停时从 `HKCU\\Software\\Hinge\\ExplorerSend\\Devices` 的本地快照枚举已完成身份握手的连接；如果 COM 宿主暂时读不到 HKCU，则回退读取当前用户 `%LOCALAPPDATA%\\Hinge\\explorer-send.txt` 的原子快照。Explorer 进程内禁止扫描网络或等待会话；没有设备时根项保持可见、子项显示禁用提示。稀疏身份包自身携带当前版本的 Shell DLL，避免仅依赖外部安装目录；连接设备时同时保留“显示更多选项”中的兼容菜单，覆盖安装/更新后 Explorer 尚未重启、暂时未重新加载第一层扩展的窗口；
- 子命令把资源管理器多选路径和稳定设备 ID 交给 Hinge 单实例，再复用 `SendFileAsync` 写入 `/storage/emulated/0/Download/Hinge/`，由 Android 按视频、图片和文件分类；机型同名时仍按设备 ID 去重；Shell 扩展优先通过当前用户专用的 `Hinge.ShellSend.v1` 命名管道交付请求，只有常驻进程不存在时才启动带参数的 Hinge 兜底；
- EXE 安装器内置签名稀疏身份包并以安装目录为 `ExternalLocation` 注册；由于自签名包必须由 Windows 信任，首次安装会通过独立提权子进程把公开证书加入 LocalMachine TrustedPeople，私钥只保留在构建机证书库。卸载时移除包身份、快照、旧式菜单并尝试删除对应公钥证书；GitHub 资产仍只有 EXE 和 ZIP，不单独发布 MSIX；
- 便携版不能静默建立本机可信包身份，继续使用 `HKCU\\Software\\Classes\\*\\shell\\HingeSend` 旧式级联菜单作为兼容回退，因此可能位于“显示更多选项”；
- 自定义接收目录保存在用户设置中，更新安装包不能回退到默认目录；
- Windows 关闭窗口时如果启用了最小化到托盘，后台运行提示由独立设置控制，默认不显示；关闭提示不影响托盘常驻、恢复、退出或其他传输/短信通知；
- 目录、微信、QQ 和最近文件的过滤不能用“文件小于某个大小”作为唯一条件，应先依据目录项类型、路径和缓存命名判断。

## 4. 通知历史约定

- Android 的 `SmsNotificationListenerService` 同时承载短信兼容转发和通用通知历史，两者使用独立开关；
- 通知历史必须先通过系统“通知访问”授权，再由用户在“工作区 > 通知历史”开启采集；正文、包名、时间、应用名称和通知 key 写入应用私有 SQLite，不写共享存储和日志；
- Windows 通过 `notificationHistory` 工作区命令按页读取，首屏最多 100 条，继续滚动才请求后续记录；默认最新在上，支持切换顺序和包名筛选；
- 通知历史数据库使用版本 2 记录所有应用通知中严格识别出的验证码；旧版本数据库通过幂等 `onUpgrade` 保留原记录，并按同一关键词 + 独立数字码规则兼容回填。Android 与 Windows 历史条目仅在确有验证码时显示“复制验证码”，普通通知不会复制正文；通知监听对单条异常厂商通知隔离处理，服务销毁时不初始化未使用的数据库；短信内容观察器在服务关闭时先停止接收回调，并安全处理关闭线程池后的竞态；
- Windows 点击微信/QQ通知时只负责唤醒桌面客户端：先检查当前 `Weixin.exe`/`WeChat.exe`/`QQ.exe`/`QQNT.exe`/`QQEX.exe` 进程。客户端已经自行显示的带标题顶层窗口只执行标准前台激活；隐藏状态绝不调用 `ShowWindow`/`ShowWindowAsync`，而是定位同进程的 `Electron_NotifyIconHostWindow`，通过 `Shell_NotifyIconGetRect` 查询真实通知图标 ID，再投递 Electron `WM_APP + 1` 的左键单击回调，由客户端自己的托盘处理器创建或恢复界面。Hinge 不绑定跨进程输入队列、不强制置顶、不显示 Chromium 内部窗口，也不发送 `mqq://`、`weixin://` 深链或回传 Android `PendingIntent`。只要匹配窗口或进程已经存在就绝不启动第二个客户端；真正启动前再次检查运行状态，只有完全没有匹配进程时才启动已安装的可执行文件；
- `notificationHistoryAction` 使用 `action=open`、`action=delete` 和 `action=clear` 三种动作；Windows 和 Android 通知历史页都支持单条删除，顶部支持确认后清空，删除后必须重新读取总数、分页和应用筛选统计；
- 新增或修改通知历史字段时，必须同步修改 `NotificationHistoryStore`、`notification_history_model.dart`、`WorkspaceRemoteClient.cs`、Windows 页面和 `protocol/notification.md`。

## 5. 开发环境

### Android

- Flutter SDK：`D:\flutter_sdk\bin\flutter.bat`；
- JDK 17；
- Android SDK、对应 build-tools 和 Bouncy Castle provider；
- 发布签名库只从被 Git 忽略的本机 `android/android/key.properties` 或 `HINGE_KEYSTORE_PATH` 读取，禁止把签名路径、别名、密码或密钥文件提交到 Git。
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

当前可交付版本为 `1.1.6`，Android build number 为 `46`，Windows 文件版本为 `1.1.6.0`。上一版公开稳定版为 `v1.0.38`；后续可交付版本继续直接使用稳定版本号，GitHub Release 直接标记为 `Latest`，不使用 `-dev`、`-pre` 后缀或 Pre-release 标签。

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
- Windows 对外只发布 `publish/windows/Hinge-Setup.exe` 和 `publish/windows/Hinge-Windows.zip`，不上传独立 MSIX / CER；构建脚本会把包含原生 Shell DLL 的签名稀疏身份组件和公钥证书内置到安装负载，用于 EXE 安装版的 Windows 11 第一层右键菜单。
- EXE 安装器在稀疏包尚未注册时给快捷方式写入未打包身份 `Hinge.Office`，供原生 Toast 使用；注册成功后必须重写开始菜单和桌面快捷方式，改用运行时真实 AUMID `Hinge.Office.Identity_29ecp0hep5z68!Hinge`。两种身份不能混写，否则任务栏会把窗口绑定到错误的图标资源。短信通知正常路径只显示在 Windows 通知中心，应用内卡片仅作为系统通知不可用时的诊断兜底。
- Windows 进程在 `App` 创建阶段、首个窗口出现前初始化 `Hinge.Office` AppUserModelId；主窗口创建后立即同时设置窗口图标和窗口类图标，再通过 `AppWindow` 加载 `Assets/app_icon.ico`。稀疏包必须提供真实尺寸的 `StoreLogo`、`Square44x44Logo`、`Square150x150Logo`，以及 16/20/24/32/40/48/64/256 像素的 `targetsize`、`altform-unplated` 资源，禁止把一张 256×256 图片直接复制成所有资源名。
- 短信/彩信通知固定使用 `Win32TrayManager` 的 `NotifyIcon.ShowBalloonTip` 托盘气泡；只有识别为验证码时才绑定点击复制动作，普通通知点击仍打开 Hinge，不再显示右上角应用内浮层。关闭窗口时的后台运行提示是独立设置，默认关闭，不能与短信/传输通知共用开关。
- 压缩规则：Windows 单文件 EXE 和便携 ZIP 由 Windows 发布脚本处理；APK 使用 Android/Flutter 构建流程的压缩与裁剪，签名完成后不得再次手工解压、重打包或压缩 APK，否则可能破坏 APK 签名。任何自动压缩步骤都必须保留在脚本中并记录在本文和 `docs/release.md`。

发布前检查 Android APK 的 V2/V3 签名、包名 `com.hinge.office`、版本名/构建号，以及 Windows 文件版本。不要把密码、签名库、`publish/` 或 `tmp/` 加入 Git。

## 8. 版本与 GitHub 发布

1. 同步 `android/pubspec.yaml`、`android/lib/core/constants.dart`、`windows/Hinge.Core/Constants.cs`、两个 Windows manifest、资源管理器 Shell 集成和 `CHANGELOG.md`；
2. 运行双端测试和构建，计算发布资产 SHA-256；
3. 提交源码、协议和文档到 `main`；
4. 后续稳定版本推送版本标签并创建 GitHub Release，直接标记为 `Latest`，上传 APK、EXE 和 ZIP；只有明确需要保留测试快照时才使用历史开发版的 Pre-release 形式；
5. 发布说明只写已经验证的内容，并明确真实设备仍需验收的边界。

## 9. 目前不能过度宣传的能力

手机投屏、真实通知回复、OCR、完整的端到端加密握手和所有 Android 厂商后台策略尚未形成“所有环境可用”的生产闭环。Mock、localhost 回环测试和单元测试只能证明局部逻辑，不能替代双端真实设备测试。

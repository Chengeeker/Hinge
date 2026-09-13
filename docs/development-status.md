# 当前开发状态

> 更新时间：2026-09-14
> 当前版本：`v1.1.6`
> 双端运行版本：Android `1.1.6+46` / Windows `1.1.6.0`

> 后续发行策略：通过验收的版本直接使用稳定版本号并标记 GitHub `Latest`，不再使用 `-dev`、`-pre` 后缀或 Pre-release 标签；当前开发版仅作为历史记录保留。

- 短信同步新增 Android 短信通知读取兼容模式、由前台服务持有的短信 Provider 观察器和端到端测试消息；
- Windows 系统横幅不可用时，仍使用窗口内通知卡片，不再将其误报为整项功能不支持。
- Android 新增“工作区 > 通知历史”：在通知访问权限和采集开关都开启后，将应用通知保存到私有 SQLite，支持时间顺序与应用筛选；Windows 新增“手机历史通知”页，按页读取并支持点击微信/QQ 原通知入口。
- 修复 Windows 点击 QQ/微信历史通知时盲目打开未注册 `mqq://` 导致系统错误对话框的问题；客户端探测现在覆盖注册表安装信息、常见安装目录和当前运行进程，并在调用 URI 前先查询协议是否已注册。
- 修复 Windows 在 QQ/微信客户端已经后台运行时重复启动进程的问题；现在会先恢复已存在的客户端窗口，避免弹出新的登录界面。
- Android 与 Windows 的历史设备自动重连使用持久化设备身份和信任记录；自动请求带有 `automaticReconnect` 标记，失败后每 5 秒重试一次，旧客户端省略该字段时仍按手动连接兼容处理。
- Windows QQ/微信通知点击链路只唤醒客户端真实界面：已显示窗口只请求前台激活；隐藏状态通过同进程的 Electron 托盘宿主和实际通知图标 ID 投递托盘单击回调，由客户端自己恢复窗口。
- 隐藏的 `Chrome_WidgetWin_0`/`Chrome_WidgetWin_1` 不再由 Hinge 调用 `ShowWindow` 或 `ShowWindowAsync`，避免产生没有任务栏入口、渲染未初始化或无法交互的假主窗口；托盘宿主只接收回调，不作为界面显示。
- 只要 QQ/微信窗口或进程已经存在就不启动第二个实例，真正启动前还会再次检查运行状态；不绑定跨进程输入队列、不强制置顶、不调用深链或 Android 原通知入口。
- Windows 安装器更新时只结束 Hinge 进程本身，不再结束其整个子进程树；由 Hinge 启动过的 QQ、微信或其他用户应用不会随 Hinge 更新而被关闭。
- Windows 资源管理器新增第一层“通过 Hinge 发送到”原生菜单：运行中的 Hinge 只发布已完成身份握手的连接，悬停时动态列出机型；多选路径由单实例接收并复用 `SendFileAsync`，目标为 Android `Download/Hinge` 自动分类目录。连接设备时同时保留“显示更多选项”中的兼容菜单，覆盖安装/更新后 Explorer 尚未重启、暂时未重新加载第一层扩展的窗口。
- 修复资源管理器选择设备后只打开 Hinge 而不发送的问题：原生 Shell 扩展现在优先经当前用户专用的 `Hinge.ShellSend.v1` 命名管道把带设备 ID 的多选请求交给已常驻的单实例，Hinge 未运行时才使用原有带参数启动兜底。
- 修复验证码托盘气泡点击后的事件串扰：复制动作会抑制同一物理交互产生的托盘打开事件，显式的托盘菜单打开命令不受影响。
- Windows 设置新增“显示后台运行提示”开关，默认关闭；关闭窗口最小化到托盘时不再重复弹出提示，但托盘常驻、恢复和退出行为不变。
- 修复 Windows 任务栏仍显示灰色占位图标的问题：现场检查确认窗口与窗口类 HICON 已是彩色资源，根因是稀疏包运行时 AUMID `Hinge.Office.Identity_29ecp0hep5z68!Hinge` 与快捷方式中的 `Hinge.Office` 不一致，且包内 44/150/Store Logo 都是错误复制的 256×256 图片。安装器现在会在包注册成功后重写快捷方式身份，构建脚本同时生成真实尺寸和各档 `targetsize` / `altform-unplated` 资源。
- 本版补齐通知历史验证码字段的 Android→Windows 转发；Windows 任务栏显式使用 `AppWindow` 图标；兼容级联菜单父项保持默认值未设置，避免回退到文件关联；EXE 安装器注册原生扩展成功后自动刷新当前用户 Explorer。

本文只记录已在仓库或构建流程中确认的状态。真实手机、真实局域网和不同厂商系统仍需单独验收。

## 技术基线

- Android：Flutter UI + Android 原生 MethodChannel / 前台服务，发布目标为 `arm64-v8a`；
- Windows：.NET 8 + Windows App SDK + WinUI 3，使用原生 `NavigationView`、Win32 托盘和系统文件关联；
- 协议：两端共享 `protocol/` 下的发现、会话、传输、剪贴板和工作区命令模型；
- 网络：UDP `52830` 发现，TCP `52831` 会话；Android 创建 socket 前绑定 Wi‑Fi，发现报文通过可选 `discoveryPort` 支持临时 UDP 端口，功能请求经过当前有效会话调度。

## 已确认可用的代码路径

### 会话和连接

- 身份握手完成后才向功能页面报告可用，降低“首页已连接、文件页等待连接”的状态分裂；
- Android 对 Socket 输出串行化，减少多个功能同时写入破坏帧边界的风险；
- Android 前台服务使用 `connectedDevice`、Wi-Fi/组播锁和 `START_STICKY`；
- Windows 对等待中的请求设置超时，并在断线时回收旧请求；
- 已知设备可以在发现后优先尝试恢复会话。

### 文件、相册和媒体

- 文件管理支持分类、类型筛选、排序、宫格/列表、选择、多选保存、删除、手机存储目录进入和返回；
- Android 目录查询返回总数，并支持 `offset` / `limit`，不再把业务结果固定截断到 300 条；
- Windows 首次只加载 200 项，滚动时继续分页；
- 图片和视频缩略图按批次读取，旧页面请求可取消；
- Windows 图片、视频、音频交给系统默认关联应用打开，关闭页面不会留下自定义播放器实例。
- Android 会在需要时用文件头校正未知 MIME，并通过原生媒体/EXIF API 返回图片尺寸、相机信息以及音视频时长、分辨率和码率；Windows 端并行请求这些轻量参数，不阻塞默认应用启动。
- Android 与 Windows 已完成大控制 JSON 的可选 ZLIB 压缩协商；只在双方声明能力且压缩后确实更小时启用，旧版本自动回退。
- Windows 外部文件拖入使用 Win32 接收回退路径，悬停时显示当前文件管理或相册目标；拖拽来源由明确的本地手势状态区分，避免应用内拖出和窗口移动误触发投放提示。
- Windows 资源管理器右键发送不在 Explorer 线程中联网；设备快照由 Hinge 会话状态更新。EXE 安装器内置并注册稀疏包身份和原生 `IExplorerCommand`，便携版继续使用“显示更多选项”中的旧式回退菜单。
- Windows 轮询 Windows GUI 线程的 `GUI_INMOVESIZE` 状态，拖动或调整其他程序窗口时屏蔽投放预览；资源管理器文件拖入不经过该状态，仍保留实时目标提示。
- Windows 应用内远程文件拖出时显示取消发送区域，取消后不会重复写入手机。

### 主题和窗口

- Android 动态取色读取系统公开的 Monet 颜色角色，关闭时使用预置主题；
- Windows 页面使用 WinUI 3 原生控件和系统字体回退；
- Windows 默认窗口尺寸按 1555×1000 设计，内容区域使用可用宽度布局；
- Windows 提供 MICA/亚克力、背景图片、启动项、静默启动和关闭到托盘设置；
- Android 提供通知、后台高耗电、锁定后台等系统保活引导。
- Android 可选短信同步已接入：明确授权后监听新到 SMS；部分设备还会使用 `READ_SMS` 观察授权后的新收件箱记录作为验证码兼容回退，短信/彩信统一通过 Windows 托盘气泡显示，只有识别为验证码时点击才复制；短信内容观察器关闭时会安全处理晚到的系统回调，避免线程池竞态导致闪退；Windows 系统通知状态可在设置中检查；彩信只转发到达提示，不读取历史收件箱或彩信正文。
- Android 通知历史与短信转发共用一个 `NotificationListenerService`，但开关和数据路径分离；通知访问权限独立于 `POST_NOTIFICATIONS`，历史正文只写入应用私有数据库。Windows 端不把历史正文落到本地文件；双端通知历史支持单条删除和确认后清空，Windows 点击微信/QQ只探测并唤醒本机客户端，不调用协议深链或回退 Android 原通知入口。

## 发布产物

- `publish/Hinge.apk`：使用固定发布签名校验后的 Android ARM64 Release APK；
- `publish/windows/Hinge-Setup.exe`：自包含、可选安装目录的 EXE 安装器；
- `publish/windows/Hinge-Windows.zip`：便携版；
- Windows 对外仍只发布自包含 EXE 安装器和便携 ZIP；签名稀疏身份包与公钥证书只作为 EXE 安装负载内部组件，不作为独立发行资产。
- Android Flutter 构建目录中的 APK 只作为中间产物；正式包必须由 `scripts/build_release_android.ps1` 重新签名并复制到 `publish/Hinge.apk`。
- 当前开发机签名库由被 Git 忽略的本机 `key.properties` 提供；签名路径、别名和密码不记录在文档或日志中。

## 自动化验证

- `flutter test --no-pub`：54 项通过；
- `flutter analyze --no-pub`：无问题；
- `dotnet build windows/Hinge.sln --configuration Release --no-restore`：通过；
- `dotnet test windows/Hinge.sln --configuration Release --no-build --no-restore`：63 项通过；
- Windows 本次 Release 构建的主程序和安装器文件版本均为 `1.1.6.0`；发布输出显式包含 `Assets/app_icon.png` 与 `Assets/app_icon.ico`，任务栏、窗口预览和安装器图标资源不再依赖未发布的外部文件；
- Android 本次本地 Release APK 目标为包名 `com.hinge.office`、版本 `1.1.6`、`versionCode 46` 和仅包含 `arm64-v8a`；
- Android 发布脚本已从本机配置读取签名信息并成功生成 `publish/Hinge.apk`；APK 为 20,171,937 字节，版本 `1.1.6+46`，V2/V3 签名校验通过，且只包含 `lib/arm64-v8a`；本次产物 SHA-256 为 `4C889096CBB86B338C3DCF1E8BDCE7163C24E8799E0405D4B062C9CF76756453`。
- Windows Release 已生成 `publish/windows/Hinge-Setup.exe`（201,430,758 字节，SHA-256 `3C5A2DFF09D3689E5143B3FEA16E9B36EF79B3842580C073F08F002833DC49EA`，文件版本 `1.1.6.0`、产品版本 `1.1.6`）和 `publish/windows/Hinge-Windows.zip`（129,664,707 字节，SHA-256 `8704B1543ECE3F389461D026468274768B76A2C266456641E2A1AF3B16D13E87`）；便携包包含 `Hinge.exe`、原生 Shell DLL 和可选身份组件，但不会自动注册身份。
- Windows 稀疏身份包本次包含 `Hinge.ShellExtension.dll` 和 19 个按真实像素生成的包图标资源，`Hinge.Identity.msix` 的 `signtool verify /pa /all` 校验通过；原生 DLL 本身不单独签名，而是作为已签名身份包的负载；旧式兼容菜单改为正确的 `ExtendedSubCommandsKey\Shell\command` 层级，一级菜单仍由包身份 + 原生 `IExplorerCommand` 提供。
- 新 EXE 已在当前开发机覆盖安装到 `D:\hinge`；安装后的 `Hinge.exe` 与构建负载 SHA-256 一致，稀疏包状态为 `Ok`，开始菜单快捷方式和 `Get-StartApps` 都解析为 `Hinge.Office.Identity_29ecp0hep5z68!Hinge`，已排除旧安装文件和旧 `Hinge.Office` 快捷方式身份继续生效的情况。
- 本版原生 Shell DLL、稀疏清单和 WinUI/安装器均完成 Release 构建；Android Dart 分析、Flutter 54 项测试、Windows 63 项 .NET 测试、Android ARM64 Release 集成构建均通过；当前开发机未完成“真实已连接手机 + Explorer 第一层悬停 + 实际文件落盘”的人工闭环，仍需安装新 EXE 后验收菜单呈现和发送结果，以及在真实短信验证码气泡上确认复制动作不恢复 Hinge 窗口。
- Android 发布脚本支持 Review 风格的本地 `android/android/key.properties` 配置：首次填写后后续构建自动读取，不再重复弹出签名输入；固定 BKS 路径仍作为未配置路径时的回退。脚本在构建/复制前校验签名输入，失败时不会覆盖 `publish/Hinge.apk`。现有 `publish/android/Hinge.apk` 是历史遗留产物，不属于当前统一输出路径，也不得作为本版正式更新包。

## 仍需真实设备验收

1. Android 不同厂商锁屏、省电、切后台和进程回收后的持续连接；
2. 大型微信/QQ 相册、文档和视频目录的读取速度与内存占用；
3. 多网卡、热点、IPv4 变化、防火墙和弱网恢复；
4. Windows 默认文件关联应用不可用时的错误提示；
5. 外部拖放在不同 UAC 权限级别、不同资源管理器和多显示器环境下的系统行为；
6. 真实身份安全、端到端加密、通用通知回复、OCR 和投屏闭环；短信同步仍需在不同 Android 厂商和安装来源上验收高敏感权限行为。

## 发布判断

本开发版适合作为真实设备试用和问题收集版本，不应宣传为覆盖所有 Android 厂商、所有网络环境或所有实验性模块的生产版。发现问题时请同时记录设备型号、Android 版本、Windows 版本、网络拓扑和对应日志。

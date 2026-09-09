# 当前开发状态

> 更新时间：2026-09-09
> 当前开发版：`v1.0.27-dev.1`
> 双端运行版本：Android `1.0.27+28` / Windows `1.0.27.0`

- 短信同步新增 Android 短信通知读取兼容模式、由前台服务持有的短信 Provider 观察器和端到端测试消息；
- Windows 系统横幅不可用时，仍使用窗口内通知卡片，不再将其误报为整项功能不支持。

本文只记录已在仓库或构建流程中确认的状态。真实手机、真实局域网和不同厂商系统仍需单独验收。

## 技术基线

- Android：Flutter UI + Android 原生 MethodChannel / 前台服务，发布目标为 `arm64-v8a`；
- Windows：.NET 8 + Windows App SDK + WinUI 3，使用原生 `NavigationView`、Win32 托盘和系统文件关联；
- 协议：两端共享 `protocol/` 下的发现、会话、传输、剪贴板和工作区命令模型；
- 网络：UDP `52830` 发现，TCP `52831` 会话，功能请求经过当前有效会话调度。

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
- Windows 轮询 Windows GUI 线程的 `GUI_INMOVESIZE` 状态，拖动或调整其他程序窗口时屏蔽投放预览；资源管理器文件拖入不经过该状态，仍保留实时目标提示。
- Windows 应用内远程文件拖出时显示取消发送区域，取消后不会重复写入手机。

### 主题和窗口

- Android 动态取色读取系统公开的 Monet 颜色角色，关闭时使用预置主题；
- Windows 页面使用 WinUI 3 原生控件和系统字体回退；
- Windows 默认窗口尺寸按 1555×1000 设计，内容区域使用可用宽度布局；
- Windows 提供 MICA/亚克力、背景图片、启动项、静默启动和关闭到托盘设置；
- Android 提供通知、后台高耗电、锁定后台等系统保活引导。
- Android 可选短信同步已接入：明确授权后监听新到 SMS；部分设备还会使用 `READ_SMS` 观察授权后的新收件箱记录作为验证码兼容回退，短信/彩信统一通过 Windows 托盘气泡显示，只有识别为验证码时点击才复制；Windows 系统通知状态可在设置中检查；彩信只转发到达提示，不读取历史收件箱或彩信正文。

## 发布产物

- `publish/android/Hinge.apk`：Android ARM64 Release APK；
- `publish/windows/Hinge-Setup.exe`：自包含、可选安装目录的 EXE 安装器；
- `publish/windows/Hinge-Windows.zip`：便携版；
- Windows 发布不包含 MSIX 或测试证书；Windows 只发布自包含 EXE 安装器和便携 ZIP。

## 自动化验证

- `dart analyze --fatal-infos`：通过；
- `flutter test --no-pub`：53 项通过；
- `dotnet build windows/Hinge.sln --configuration Release --no-restore`：通过；
- `dotnet test windows/Hinge.sln --configuration Release --no-build --no-restore`：63 项通过；
- Windows 本次本地打包的文件版本为 `1.0.27.0`，同时生成 EXE 安装器和便携 ZIP；
- Android APK V2/V3 签名验证：通过。

## 仍需真实设备验收

1. Android 不同厂商锁屏、省电、切后台和进程回收后的持续连接；
2. 大型微信/QQ 相册、文档和视频目录的读取速度与内存占用；
3. 多网卡、热点、IPv4 变化、防火墙和弱网恢复；
4. Windows 默认文件关联应用不可用时的错误提示；
5. 外部拖放在不同 UAC 权限级别、不同资源管理器和多显示器环境下的系统行为；
6. 真实身份安全、端到端加密、通用通知回复、OCR 和投屏闭环；短信同步仍需在不同 Android 厂商和安装来源上验收高敏感权限行为。

## 发布判断

本开发版适合作为真实设备试用和问题收集版本，不应宣传为覆盖所有 Android 厂商、所有网络环境或所有实验性模块的生产版。发现问题时请同时记录设备型号、Android 版本、Windows 版本、网络拓扑和对应日志。

# Hinge

Hinge 是一个局域网优先的 Android + Windows 跨设备工作台，用于在手机与 Windows 电脑之间发现设备、建立会话、传输文件，并查看手机上的轻量工作区数据。

- 项目地址：[github.com/Chengeeker/Hinge](https://github.com/Chengeeker/Hinge)
- 当前开发版：[v1.0.17-dev.1](https://github.com/Chengeeker/Hinge/releases/tag/v1.0.17-dev.1)（安装包运行版本为 `1.0.17`）
- 许可证：[MIT](LICENSE)

## 这是什么

Hinge 不依赖账号和云端中转。设备发现、会话和文件传输默认发生在同一局域网中：Windows 端负责桌面文件管理和系统集成，Android 端负责手机数据、后台连接与移动端工作区。

项目的设计原则是：

- LAN-first：局域网直连优先，不把用户数据上传到项目服务器；
- Native-first：Windows 使用 WinUI 3 / Windows App SDK，媒体文件交给 Windows 默认关联应用打开；Android 使用 Flutter UI 和 Android 原生桥接；
- 事实优先：Mock、回环测试和真实手机验收分开记录，不把自动化测试当作所有机型都已验证。

## 当前版本能做什么

### 跨设备基础能力

- UDP 局域网发现和手动设备发现；
- 已知设备快速重连、心跳和断线状态恢复；
- 文本、文件和剪贴板同步；
- 前台服务、通知和厂商后台保活设置引导；
- 统一的跨端协议、传输模型和测试基线。

### Android

- 首页、工作区、设置三段式底栏；
- 笔记、待办、日历和相册入口；
- 读取日历、MediaStore、手机存储和系统统计信息；
- 相册按批次读取，避免大型媒体库一次性阻塞界面；
- Material You / Monet 动态颜色、浅色/深色模式、纯黑深色模式和预置主题；
- 自定义存储路径、通知常驻和后台保活设置入口；
- 使用稳定签名库生成可覆盖更新的 ARM64 APK。

### Windows

- .NET 8 + Windows App SDK + WinUI 3 原生桌面客户端；
- 独立的首页、文件管理、笔记、待办、日历、相册、工具和设置页面；
- 显示手机设备名称、品牌标识和连接状态；
- 文件管理支持最近文件、图片、视频、音频、文档、微信相册、QQ 相册和手机存储；
- 宫格/列表视图、类型筛选、排序、多选、保存、删除、目录进入/返回和分页加载；
- 相册和图片缩略图按批次加载，首次只取前 200 项，继续滚动时再读取后续内容；
- 支持从资源管理器拖入文件，并在拖动过程中预览保存目标；窗口标题栏移动不会误触发文件投放提示；
- 图片、视频、音频使用当前 Windows 文件关联打开，避免应用内播放器的后台播放和释放问题；
- WinUI 3 主题、窗口材质、背景图、开机启动、静默启动和关闭时最小化到托盘；
- 推荐使用可选择安装路径的自包含 EXE 安装器，也提供便携 ZIP 和可选 MSIX。

## 当前明确不包含的能力

- 手机投屏没有作为本版本的可用功能交付，Windows 端不会显示虚假的投屏画面；
- 通知回复、真实屏幕编码/解码和 OCR 仍属于后续原生能力接入范围；
- Android 厂商的省电、锁屏和后台回收策略无法由应用完全绕过，需要用户按系统引导放行；
- 大型微信/QQ 媒体库、文档目录、多网卡、弱网和不同厂商后台策略仍需要更多真实设备验收。

## 安装

### Android

从 [v1.0.17-dev.1 Release](https://github.com/Chengeeker/Hinge/releases/tag/v1.0.17-dev.1) 下载 `Hinge.apk`。首次运行时按系统提示授予日历、照片/视频、通知和后台运行相关权限；若设备使用严格的电池策略，还需要把应用加入后台高耗电或锁定后台清单。

### Windows

推荐下载 `Hinge-Setup.exe`。这是自包含 EXE 安装器，安装时可以选择目标目录，不依赖 MSIX 测试证书。

另外提供：

- `Hinge-Windows.zip`：解压即用的便携版；
- `Hinge-Installer.msix`：适合已经配置好测试证书的环境；
- `Hinge-Installer.cer`：MSIX 测试证书，仅在确认来源可信时安装。

Hinge 不会修改 `EnableLUA`，也不要求用户把它改成 `1`。保持系统原有设置即可；应用同时保留 Win32 文件拖放回退路径，在 `EnableLUA=0` 的环境下也能接收普通资源管理器文件拖放。若应用被“以管理员身份运行”而资源管理器不是管理员权限，Windows 仍可能按系统权限规则拒绝拖放，此时应让两者处于相同权限级别，而不是修改注册表。可参考 [Windows drag-and-drop 文档](https://learn.microsoft.com/en-us/windows/apps/develop/data/drag-and-drop)。

## 本地开发

### Android

```powershell
cd android
flutter pub get
flutter analyze --no-pub
flutter test --no-pub
flutter run
```

发布 APK 需要本机签名库，不要把签名库、密码或临时转换文件提交到仓库：

```powershell
$env:HINGE_KEYSTORE_PASSWORD = '<keystore-password>'
$env:HINGE_KEY_PASSWORD = '<key-password>'
.\scripts\build_release_android.ps1 `
  -KeystorePath 'D:\path\to\infinitycm.bks' `
  -KeyAlias 'infinitycm'
```

### Windows

要求 Windows 10/11 x64、.NET 8 SDK、Windows SDK、Visual Studio C++ 桌面开发工具和 Windows App SDK / WinUI 3 构建环境。

```powershell
dotnet build windows/Hinge.sln --configuration Release
dotnet test windows/Hinge.sln --configuration Release
.\scripts\build_release_windows.ps1
```

输出目录为 `publish/`。该目录和 `tmp/` 已被 `.gitignore` 排除，发布资产通过 GitHub Release 分发，不直接塞进源代码提交。

## 仓库结构

```text
android/                 Flutter Android 客户端与 Android 原生桥
windows/                 WinUI 3 客户端、核心层、平台层和测试
protocol/                跨端协议和专题说明
docs/                    架构、开发状态、兼容性和发布流程
installer/               EXE 安装器源码
scripts/                 构建、防火墙和卸载脚本
tests/                   跨模块测试资料
```

## 文档入口

- [开发文档](docs/development.md)：从源码、协议、测试到发布的日常开发入口；
- [开发状态](docs/development-status.md)：当前实现、验证结果和真实设备边界；
- [系统架构](docs/architecture.md)：UI、Feature、Core、协议和平台层边界；
- [兼容性基准](docs/compatibility.md)：Android 厂商、Windows 网络和权限风险；
- [发布流程](docs/release.md)：版本、签名、构建产物和 GitHub Release 约定；
- [依赖说明](docs/dependencies.md)：第三方依赖和许可证；
- [协议说明](protocol/protocol.md)：跨端帧和传输约定；
- [安全策略](SECURITY.md)：局域网边界、报告问题和敏感配置；
- [更新日志](CHANGELOG.md)：面向用户的版本变更。

## 贡献

写代码前先确认功能是否已经存在、系统原生 API 或现成依赖是否能够解决，以及是否真的需要新增实现。跨端功能应同步更新协议、测试和文档；不要把单端 Mock 或回环测试描述成真实设备已经完成。

详见 [CONTRIBUTING.md](CONTRIBUTING.md)。

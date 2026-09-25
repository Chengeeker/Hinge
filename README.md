# Hinge

Hinge 是一个局域网优先的 Android + Windows 跨设备工作台，用于在手机与 Windows 电脑之间发现设备、建立会话、传输文件，并查看手机上的轻量工作区数据。

- 项目地址：[github.com/Chengeeker/Hinge](https://github.com/Chengeeker/Hinge)
- 本次发行版本：Android `1.4.0+99`（arm64-v8a）；Windows `1.4.0.0`。重点完善 Cloud Relay 文件传输进度与 Android 传输记录；剪贴板跨设备同步已移除。
- 许可证：[MIT](LICENSE)

## 这是什么

Hinge 不依赖账号或 Hinge 官方云服务。设备发现、会话和文件传输默认发生在同一局域网中；用户可以自行部署可选的 Cloud Relay，把已经信任的设备之间的文件异步放入自己的 Cloudflare Worker + R2。Windows 端负责桌面文件管理和系统集成，Android 端负责手机数据、后台连接与移动端工作区。

项目的设计原则是：

- LAN-first：局域网直连优先，不把用户数据上传到项目服务器；
- 可选 Cloud Relay：只有用户主动配置并开启自己的 Worker 后，LAN 不可用时才上传加密文件；Hinge 不托管 Relay，也不支持互联网首次配对；
- Native-first：Windows 使用 WinUI 3 / Windows App SDK，媒体文件交给 Windows 默认关联应用打开；Android 使用 Flutter UI 和 Android 原生桥接；
- 事实优先：Mock、回环测试和真实手机验收分开记录，不把自动化测试当作所有机型都已验证。

## 当前版本包含的功能

### 跨设备基础能力

- UDP 局域网发现和手动设备发现；
- 已知设备快速重连、心跳和断线状态恢复；已经建立过会话且仍在信任库中的设备，在更新或短暂断线后重新上线时自动恢复连接，新设备仍需手动连接；
- 文本消息与文件传输；跨设备剪贴板同步已移除；
- Android 首页提供本地文件传输记录，显示局域网/Cloud Relay 收发方向、状态与进度；可删除或清理已结束记录；
- 前台服务、通知和厂商后台保活设置引导；
- Android 支持低功耗待命，Windows 右键发送时可通过 BLE Companion presence 请求恢复局域网连接；BLE 只负责唤醒，不承载文件内容，自动唤醒不可用时由 Android 常驻通知作为人工兜底；Android 系统分享在已有会话可用时直接进入发送，断联时才显示待发送队列；
- 统一的跨端协议、传输模型和测试基线。

### Android

- 首页、工作区、设置三段式底栏；
- 笔记、待办、日历和相册入口；
- 读取日历、MediaStore、手机存储和系统统计信息；
- 相册按批次读取，避免大型媒体库一次性阻塞界面；
- Material You / Monet 动态颜色、浅色/深色模式、纯黑深色模式和标准 Material 3 预置色板；
- 自定义存储路径、通知常驻和后台保活设置入口；
- 原生前台服务负责局域网 TCP、心跳、断线重连和待发送队列；息屏静止后进入 `Suspended`/低功耗待命，保留可恢复会话，不把普通第三方应用承诺为永久 TCP 保活；
- 保活设置支持 Companion Device 绑定、解除绑定和能力状态；常驻通知在活跃和待命阶段使用不同状态文案，并提供点击唤醒入口；
- 可选的短信同步：将新到 SMS 转发到可信 Windows 会话，并使用托盘气泡通知；只有识别为验证码的短信/彩信，点击整条气泡才会复制验证码，普通通知不会复制；默认关闭；可申请接收短信和访问短信/彩信权限，仅观察授权后的新消息，不扫描历史收件箱；
- 工作区“通知历史”：在 Android 系统通知访问权限和采集开关都开启后，按时间记录应用通知、应用图标、应用名称、标题和正文，默认按最新到最早显示，支持切换顺序、按应用筛选；对所有应用通知使用严格的验证码关键词与独立数字码规则，识别到验证码时可在历史条目直接复制；记录保存在应用私有数据库，默认关闭；
- 使用稳定签名库生成可覆盖更新的 ARM64 APK。

### Windows

- .NET 8 + Windows App SDK + WinUI 3 原生桌面客户端；
- 独立的首页、文件管理、笔记、待办、日历、相册、工具和设置页面；
- 侧边栏“手机历史通知”：从已连接的 Android 设备分页读取通知历史，默认按最新到最早显示，支持时间顺序、应用筛选和验证码复制；微信/QQ只唤醒已运行的桌面客户端主窗口，避免重复启动到新的登录界面；
- 显示手机设备名称、品牌标识和连接状态；
- 首页提供独立传输记录区块：显示发送/接收进度，手机未连接时显示待发送任务；可取消进行中的发送，单条删除历史记录或清空已完成、失败和已取消记录；
- 文件管理支持最近文件、图片、视频、音频、文档、微信相册、QQ 相册和手机存储；
- 文件分类不只依赖手机厂商返回的 MIME：对常见文档、压缩包、安装包和媒体扩展名做统一回退识别，未知类型在需要时再读取文件头；
- 大型工作区/同步控制数据支持双方协商的 ZLIB 压缩，旧版本会自动回退到普通控制帧；
- 文件传输沿用 LAN TCP 帧协议，双方支持时使用边读边校验的 SHA-256、3 个 2 MiB 缓冲块有限 read-ahead 和最终帧直写，减少传输前整文件预扫描、重复大块复制和磁盘/网络空转；旧版本对端自动回退到原有预校验路径；
- Cloud Relay 文件中转可在设置中开启：使用用户自建的 Cloudflare Worker + 私有 R2 和独立的 [Hinge-Relay](https://github.com/Chengeeker/Hinge-Relay)，客户端在上传前用 AES-256-GCM 加密元数据和每个 8 MiB 文件分块；Android 通知栏显示云上传/下载进度，局域网恢复前，已上传文件会显示为“等待设备接收”；
- Windows 首页传输状态会显示测得的有效字节速率，Android 原生连接诊断会记录耗时、字节数和吞吐，方便区分 Wi-Fi、手机写盘与协议处理瓶颈；
- 宫格/列表视图、类型筛选、排序、多选、保存、删除、目录进入/返回和分页加载；
- 相册和图片缩略图按批次加载，首次只取前 200 项，继续滚动时再读取后续内容；
- 双击媒体时按需读取图片尺寸、EXIF 相机信息，或音视频时长、分辨率、码率，并将结果反馈到 Windows 状态栏；
- 支持从资源管理器拖入文件，并在拖动过程中预览保存目标；窗口标题栏移动不会误触发文件投放提示；
- 支持在 Hinge 运行且手机已连接时，从 Windows 11 第一层右键菜单的“通过 Hinge 发送到”悬停选择目标机型；支持资源管理器多选，复用同一传输链路并自动保存到 Android `/storage/emulated/0/Download/Hinge/` 下的类型目录。设备快照读取失败时根项仍保留并显示禁用提示；EXE 安装版把 Shell DLL 放入签名稀疏身份包；便携版未注册包身份时使用“显示更多选项”中的兼容菜单；
- Windows 右键发送在手机未连接时会先保存到持久化队列，并尝试通过 BLE 唤醒已关联的 Android 设备；BLE、适配器或厂商后台策略不可用时，用户点击 Android 常驻通知即可恢复连接并继续发送；
- 图片、视频、音频使用当前 Windows 文件关联打开，避免应用内播放器的后台播放和释放问题；
- WinUI 3 主题、窗口材质、背景图、开机启动、静默启动和关闭时最小化到托盘；后台运行提示可在设置中单独控制，默认关闭；
- 推荐使用可选择安装路径的自包含 EXE 安装器，也提供便携 ZIP。

## 当前明确不包含的能力

- 手机投屏没有作为本版本的可用功能交付，Windows 端不会显示虚假的投屏画面；
- 通知回复、真实屏幕编码/解码和 OCR 仍属于后续原生能力接入范围；
- Android 厂商的省电、锁屏和后台回收策略无法由应用完全绕过，需要用户按系统引导放行；
- 不保证普通第三方 Android 应用在所有厂商设备上锁屏静置数小时仍保持同一条 TCP；BLE Companion 自动唤醒也受 Windows 蓝牙适配器、Android 系统关联和厂商后台策略影响；Cloud Relay 也不保证 Android 被系统完全停止时立即下载；
- 大型微信/QQ 媒体库、文档目录、多网卡、弱网和不同厂商后台策略仍需要更多真实设备验收。

## 安装

### Android

从 [GitHub Releases](https://github.com/Chengeeker/Hinge/releases) 下载 `Hinge.apk`。首次运行时按系统提示授予日历、照片/视频、通知和后台运行相关权限；“通知历史”还需要在 Android 系统的“通知访问”设置中明确允许 Hinge，并在工作区内开启采集；短信转发会额外引导申请 `RECEIVE_SMS` 和 `READ_SMS`，彩信还需要对应的 MMS/WAP 权限。Hinge 只观察授权之后的新消息，不扫描历史收件箱；通知历史正文只保存在应用私有数据库。若要使用 Windows 右键发送的自动唤醒，请先在 Windows 设置中开启 BLE 配对广播，再在 Android“保活设置”中完成 Companion 设备关联；未完成关联时仍可点击常驻通知手动唤醒。部分 Android/厂商/安装来源可能拒绝高敏感短信权限，此时仍会保留系统允许的实时广播路径。若设备使用严格的电池策略，还需要把应用加入后台高耗电或锁定后台清单。

### Windows

推荐下载 `Hinge-Setup.exe`。这是自包含 EXE 安装器，安装时可以选择目标目录；为了注册 Windows 11 第一层资源管理器菜单，首次安装会请求一次管理员确认，仅把随安装器提供的 Hinge 公钥证书加入本机受信任人，签名私钥不会随包分发。

若要启用右键发送时的 BLE 自动唤醒，请在 Hinge 设置中开始 BLE 配对广播，并在 Android“保活设置”中完成 Companion 关联。BLE 自动唤醒不是文件传输通道；Windows 和 Android 仍通过局域网 TCP 传输文件，BLE 不可用时可使用 Android 常驻通知作为兜底。

### 可选 Cloud Relay

Cloud Relay 不是 Hinge 官方服务。需要先把 [`Hinge-Relay`](https://github.com/Chengeeker/Hinge-Relay) 导入自己的 GitHub 仓库，在 Cloudflare Worker 中绑定私有 R2、设置 `RELAY_ADMIN_TOKEN` 后部署；再在 Windows 和 Android 的 Cloud Relay 设置中填写 Worker 地址、使用同一个 Relay 密钥，并分别注册两个已经在 Hinge 中信任的设备。Relay 密钥不会上传 Worker；文件名、大小和摘要在客户端加密后才进入 R2。跨设备剪贴板同步及 `/v1/clipboard` Relay API 已从当前源码移除。

Cloud Relay 只做异步文件中转：当 LAN 会话可用时仍走原有 TCP；没有 LAN 时，文件等待接收端轮询，Android 被系统完全停止时不会承诺即时唤醒。部署、API、更新同步和密钥边界见独立项目的 [README](https://github.com/Chengeeker/Hinge-Relay#readme) 与 [协议说明](protocol/cloud-relay.md)。

另外提供：

- `Hinge-Windows.zip`：解压即用的便携版；

Windows 发布只提供上面的 EXE 安装器和便携 ZIP，不单独提供 MSIX 资产。EXE 内部携带签名的稀疏身份组件；便携 ZIP 不自动修改本机证书或包注册，因此它的右键菜单属于兼容模式。

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

第一次在本机复制 `android/android/key.properties.example` 为
`android/android/key.properties`，填写本机签名库信息即可。这个文件已被
`.gitignore` 忽略，之后构建不再弹出签名输入窗口；其他机器只需要各自配置
自己的签名文件，不要复制或提交他人的签名信息。

```powershell
Copy-Item android/android/key.properties.example android/android/key.properties
# 编辑 android/android/key.properties，填写 storePassword、keyAlias、keyPassword
.\scripts\build_release_android.ps1
```

脚本会读取被 Git 忽略的本机 `key.properties`，并将签名后的 ARM64 APK 固定输出为 `publish/Hinge.apk`；`android/build/app/outputs/flutter-apk/app-release.apk` 只是中间产物，不要直接分发。BKS 会在构建期间转换为临时 PKCS12，完成后立即清理。缺少签名输入时脚本会停止，不会用调试签名覆盖发布包。APK 签名后不要再次手工压缩或重打包；Windows EXE/ZIP 的压缩由 Windows 发布脚本自动完成。签名库、路径、密码和临时转换文件不会写入 README 或 Git。

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
installer/               EXE 安装器源码
scripts/                 构建、防火墙和卸载脚本
tests/                   跨模块测试资料
```

## 文档入口

- 开发文档：本机统一入口为 `D:\App\开发文档\Hinge.md`，不随仓库发布；
- [第三方许可证登记](THIRD_PARTY_LICENSES.md)：实际随项目发布或参与构建的依赖清单；
- [协议说明](protocol/protocol.md)：跨端帧和传输约定；
- [Cloud Relay 协议说明](protocol/cloud-relay.md)：独立 Worker/R2 API、端到端加密格式和信任边界；
- [Hinge-Relay 独立项目](https://github.com/Chengeeker/Hinge-Relay)：部署、GitHub 同步和 Cloudflare 配置；
- [安全策略](SECURITY.md)：局域网边界、报告问题和敏感配置；
- [更新日志](CHANGELOG.md)：面向用户的版本变更。

## 贡献

写代码前先确认功能是否已经存在、系统原生 API 或现成依赖是否能够解决，以及是否真的需要新增实现。跨端功能应同步更新协议、测试和文档；不要把单端 Mock 或回环测试描述成真实设备已经完成。

详见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 开源组件与合规说明

Hinge 的业务代码采用 [MIT License](LICENSE)。下面列出的是 Hinge 直接声明、参与构建或随运行时使用的主要开源组件；精确版本以 `android/pubspec.lock`、各 `.csproj` 和构建工具锁定结果为准。间接依赖由 Flutter/Dart、NuGet 和 Android 构建工具解析，不在 README 中重复抄录，审查入口见 [第三方许可证登记](THIRD_PARTY_LICENSES.md)。

Windows 端 QQ/微信托盘唤醒兼容逻辑参考了 [Electron](https://github.com/electron/electron)（MIT）公开的 `NotifyIconHost` 消息分发实现，用于识别 `Electron_NotifyIconHostWindow`、通知图标 ID 和托盘单击回调。Hinge 没有复制或打包 Electron 源码，也没有新增 Electron 运行时依赖；详细边界见 [第三方许可证登记](THIRD_PARTY_LICENSES.md)。

Windows 11 第一层资源管理器菜单的 COM 激活框架改编自 Microsoft 的 [vscode-explorer-command](https://github.com/microsoft/vscode-explorer-command)（MIT）。Hinge 在此基础上自行实现本地连接快照、动态机型子菜单、多文件参数传递和单实例发送；对应版权与使用边界已登记在 [第三方许可证登记](THIRD_PARTY_LICENSES.md)。

### 随应用使用的直接组件

| 组件 | 平台与版本 | 许可证 | 在 Hinge 中的用途 |
| --- | --- | --- | --- |
| [Flutter SDK](https://github.com/flutter/flutter) | Android / 构建环境 | BSD-3-Clause | Flutter UI、国际化和应用运行框架 |
| [`crypto`](https://pub.dev/packages/crypto) | Android / Dart `3.0.7` | BSD-3-Clause | SHA-256、HMAC 和文件完整性校验 |
| [`dynamic_color`](https://pub.dev/packages/dynamic_color) | Android / Dart `1.9.0` | Apache-2.0 | 接入 Android 12+ Monet 动态配色 |
| [`material_symbols_icons`](https://pub.dev/packages/material_symbols_icons) | Android / Dart `4.2960.0` | Apache-2.0 | Material Symbols 图标 |
| [`flutter_svg`](https://pub.dev/packages/flutter_svg) | Android / Dart `2.3.0` | MIT | 读取本地品牌 SVG 资源 |
| [`cupertino_icons`](https://pub.dev/packages/cupertino_icons) | Android / Dart `1.0.9` | MIT | 少量兼容性图标资源 |
| [Microsoft.WindowsAppSDK](https://github.com/microsoft/WindowsAppSDK) | Windows / NuGet `2.4.0` | MIT | WinUI 3、窗口和 Windows App SDK 能力 |
| [.NET Runtime / BCL](https://github.com/dotnet/runtime) | Windows / .NET `8` | MIT | Socket、压缩、加密、文件和系统集成基础能力 |

### 仅用于开发和测试的组件

Windows 测试使用 [xUnit](https://github.com/xunit/xunit) `2.5.3`、[Microsoft.NET.Test.Sdk](https://github.com/microsoft/vstest) `17.8.0`、[coverlet.collector](https://github.com/coverlet-coverage/coverlet) `6.0.0` 和 `xunit.runner.visualstudio` `2.5.3`；Android 测试与代码检查使用 Flutter SDK 内的 `flutter_test` 和 `flutter_lints`。这些组件不会被打包进最终用户运行时。

### 依赖选型说明

在做文件类型、媒体信息和压缩功能的技术选型时，我们参考过一些公开的项目清单，其中包括 vivo 办公套件公开页面列出的组件。这些材料只用于了解可选方案，不代表 Hinge 使用了 vivo 办公套件的实现。当前版本没有引入 `file-type`、`mediainfo.js`/MediaInfo、`ExifReader`、`fflate`、`pako` 或 `Jimp`，也没有复制它们的源码、打包文件或依赖配置：

- MIME/文件头识别使用 Hinge 自有的 Kotlin 小型实现；
- 音视频信息使用 Android Framework `MediaMetadataRetriever`；
- 图片尺寸与 EXIF 使用 Android Framework `BitmapFactory` / `ExifInterface`；
- Windows 媒体打开、文件属性和压缩使用 WinRT / .NET BCL。

如果后续确实引入新的第三方组件，会在提交中固定版本，并在 `THIRD_PARTY_LICENSES.md` 中记录许可证和 NOTICE 要求。

### 品牌 SVG 与签名材料

Windows 和 Android 使用的本地品牌 SVG 来自 Wikimedia Commons 对应条目，具体来源在 [第三方许可证登记](THIRD_PARTY_LICENSES.md) 中列出。它们属于各品牌商标，仅用于连接设备时的品牌识别，不表示 Hinge 与品牌存在合作或背书关系。

Android 发布包必须由发布者自行提供签名库、别名和密码。签名库、密码、临时转换文件和个人设备信息不进入 Git，也不会写入 README、发行版或构建日志；构建脚本只读取调用者提供的环境变量或参数。

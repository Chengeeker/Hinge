# Hinge

Hinge 是一个局域网优先的 Android + Windows 跨设备工作台，用于在手机与 Windows 电脑之间发现设备、建立会话、传输文件，并查看手机上的轻量工作区数据。

- 项目地址：[github.com/Chengeeker/Hinge](https://github.com/Chengeeker/Hinge)
- 当前开发版：`v1.0.32-dev.1`（安装包运行版本为 `1.0.32`）
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
- 已知设备快速重连、心跳和断线状态恢复；已经建立过会话且仍在信任库中的设备，在更新或短暂断线后重新上线时自动恢复连接，新设备仍需手动连接；
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
- 可选的短信同步：将新到 SMS 转发到可信 Windows 会话，并使用托盘气泡通知；只有识别为验证码的短信/彩信，点击整条气泡才会复制验证码，普通通知不会复制；默认关闭；可申请接收短信和访问短信/彩信权限，仅观察授权后的新消息，不扫描历史收件箱；
- 工作区“通知历史”：在 Android 系统通知访问权限和采集开关都开启后，按时间记录应用通知、应用图标、应用名称、标题和正文，支持正序/倒序及按应用筛选；记录保存在应用私有数据库，默认关闭；
- 使用稳定签名库生成可覆盖更新的 ARM64 APK。

### Windows

- .NET 8 + Windows App SDK + WinUI 3 原生桌面客户端；
- 独立的首页、文件管理、笔记、待办、日历、相册、工具和设置页面；
- 侧边栏“手机历史通知”：从已连接的 Android 设备分页读取通知历史，支持时间顺序和应用筛选；微信/QQ只唤醒已运行的桌面客户端主窗口，避免重复启动到新的登录界面；
- 显示手机设备名称、品牌标识和连接状态；
- 文件管理支持最近文件、图片、视频、音频、文档、微信相册、QQ 相册和手机存储；
- 文件分类不只依赖手机厂商返回的 MIME：对常见文档、压缩包、安装包和媒体扩展名做统一回退识别，未知类型在需要时再读取文件头；
- 大型工作区/同步控制数据支持双方协商的 ZLIB 压缩，旧版本会自动回退到普通控制帧；
- 宫格/列表视图、类型筛选、排序、多选、保存、删除、目录进入/返回和分页加载；
- 相册和图片缩略图按批次加载，首次只取前 200 项，继续滚动时再读取后续内容；
- 双击媒体时按需读取图片尺寸、EXIF 相机信息，或音视频时长、分辨率、码率，并将结果反馈到 Windows 状态栏；
- 支持从资源管理器拖入文件，并在拖动过程中预览保存目标；窗口标题栏移动不会误触发文件投放提示；
- 图片、视频、音频使用当前 Windows 文件关联打开，避免应用内播放器的后台播放和释放问题；
- WinUI 3 主题、窗口材质、背景图、开机启动、静默启动和关闭时最小化到托盘；
- 推荐使用可选择安装路径的自包含 EXE 安装器，也提供便携 ZIP。

## 当前明确不包含的能力

- 手机投屏没有作为本版本的可用功能交付，Windows 端不会显示虚假的投屏画面；
- 通知回复、真实屏幕编码/解码和 OCR 仍属于后续原生能力接入范围；
- Android 厂商的省电、锁屏和后台回收策略无法由应用完全绕过，需要用户按系统引导放行；
- 大型微信/QQ 媒体库、文档目录、多网卡、弱网和不同厂商后台策略仍需要更多真实设备验收。

## 安装

### Android

从 [GitHub Releases](https://github.com/Chengeeker/Hinge/releases) 下载 `Hinge.apk`。首次运行时按系统提示授予日历、照片/视频、通知和后台运行相关权限；“通知历史”还需要在 Android 系统的“通知访问”设置中明确允许 Hinge，并在工作区内开启采集；短信转发会额外引导申请 `RECEIVE_SMS` 和 `READ_SMS`，彩信还需要对应的 MMS/WAP 权限。Hinge 只观察授权之后的新消息，不扫描历史收件箱；通知历史正文只保存在应用私有数据库。部分 Android/厂商/安装来源可能拒绝高敏感短信权限，此时仍会保留系统允许的实时广播路径。若设备使用严格的电池策略，还需要把应用加入后台高耗电或锁定后台清单。

### Windows

推荐下载 `Hinge-Setup.exe`。这是自包含 EXE 安装器，安装时可以选择目标目录，不依赖 MSIX 测试证书。

另外提供：

- `Hinge-Windows.zip`：解压即用的便携版；

Windows 发布只提供上面的 EXE 安装器和便携 ZIP，不提供需要测试证书的 MSIX 发布资产。

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

脚本会读取本机 `key.properties`，默认使用 `D:\Download\backup\infinitycm.bks`，并将签名后的 ARM64 APK 固定输出为 `publish/Hinge.apk`；`android/build/app/outputs/flutter-apk/app-release.apk` 只是中间产物，不要直接分发。BKS 会在构建期间转换为临时 PKCS12，完成后立即清理。缺少签名输入时脚本会停止，不会用调试签名覆盖发布包。APK 签名后不要再次手工压缩或重打包；Windows EXE/ZIP 的压缩由 Windows 发布脚本自动完成。签名库、密码和临时转换文件不会写入 README 或 Git。

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
- [第三方许可证登记](THIRD_PARTY_LICENSES.md)：实际随项目发布或参与构建的依赖清单；
- [协议说明](protocol/protocol.md)：跨端帧和传输约定；
- [安全策略](SECURITY.md)：局域网边界、报告问题和敏感配置；
- [更新日志](CHANGELOG.md)：面向用户的版本变更。

## 贡献

写代码前先确认功能是否已经存在、系统原生 API 或现成依赖是否能够解决，以及是否真的需要新增实现。跨端功能应同步更新协议、测试和文档；不要把单端 Mock 或回环测试描述成真实设备已经完成。

详见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 开源组件与合规说明

Hinge 的业务代码采用 [MIT License](LICENSE)。下面列出的是 Hinge 直接声明、参与构建或随运行时使用的主要开源组件；精确版本以 `android/pubspec.lock`、各 `.csproj` 和构建工具锁定结果为准。间接依赖由 Flutter/Dart、NuGet 和 Android 构建工具解析，不在 README 中重复抄录，审查入口见 [第三方许可证登记](THIRD_PARTY_LICENSES.md)。

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

### vivo 开源声明的审查边界

参考 vivo 办公套件公开的开源声明时，评估过 `file-type`、`mediainfo.js`/MediaInfo、`ExifReader`、`fflate`、`pako` 和 `Jimp`。它们只是候选方案和实现思路的对照，当前没有复制源码、打包文件或写入 Hinge 的依赖锁文件：

- MIME/文件头识别使用 Hinge 自有的 Kotlin 小型实现；
- 音视频信息使用 Android Framework `MediaMetadataRetriever`；
- 图片尺寸与 EXIF 使用 Android Framework `BitmapFactory` / `ExifInterface`；
- Windows 媒体打开、文件属性和压缩使用 WinRT / .NET BCL。

如果未来真正引入上述项目或任何新的第三方组件，必须在同一个提交中锁定版本、登记许可证和 NOTICE 要求，并同步更新 `docs/dependencies.md`、`THIRD_PARTY_LICENSES.md` 和本节。

### 品牌 SVG 与签名材料

Windows 和 Android 使用的本地品牌 SVG 来自 Wikimedia Commons 对应条目，具体来源在 [第三方许可证登记](THIRD_PARTY_LICENSES.md) 中列出。它们属于各品牌商标，仅用于连接设备时的品牌识别，不表示 Hinge 与品牌存在合作或背书关系。

Android 发布包必须由发布者自行提供签名库、别名和密码。签名库、密码、临时转换文件和个人设备信息不进入 Git，也不会写入 README、发行版或构建日志；构建脚本只读取调用者提供的环境变量或参数。

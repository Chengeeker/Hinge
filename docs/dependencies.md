# 依赖准入与审核规范 (Dependency Policy)

## 1. 准入核心准则
每引入一个新依赖包，开发者必须书面回答以下 5 个问题：
1. **操作系统原生 API 能否直接实现？** (若能，严禁引入第三方库)
2. **项目工程内是否已有类似工具或已存在依赖？**
3. **该依赖是否采用 MIT / BSD 宽松许可证？** (GPL/AGPL/MPL 一票否决)
4. **该依赖的体积、活跃度和安全性评级如何？**
5. **是否增加攻击面或维护负担？**

## 2. 依赖管理流程
- 任何新增依赖须在 PR 中提交 docs/dependencies.md 与 THIRD_PARTY_LICENSES.md 的更新。
- 依赖版本统一锁定，禁止使用过于宽泛的版本范围。

## 3. 已准入依赖库清单

| 依赖名称 | 平台 | 许可证 | 准入理由 |
| :--- | :--- | :--- | :--- |
| `crypto` | Android / Dart | BSD-3-Clause | 纯 Dart SHA-256/HMAC 与文件完整性校验 |
| `dynamic_color` | Android / Dart | Apache-2.0 | 读取系统动态配色的 Flutter 接口；实际 Monet 角色由 Android 原生资源桥提供 |
| `material_symbols_icons` | Android / Dart | Apache-2.0 | Android 端 Material Symbols 图标；Windows 端优先使用 WinUI/Segoe MDL2 原生符号 |
| `flutter_svg` | Android / Dart | MIT | 读取本地品牌 SVG 资源；不从网络加载运行时 Logo |
| `cupertino_icons` | Android / Dart | MIT | Flutter 工程默认的辅助图标资源，目前仅少量兼容性使用 |
| `Microsoft.WindowsAppSDK` | Windows / NuGet | MIT | WinUI 3 / Windows App SDK 运行时与原生控件桥接 |
| `.NET BCL` | Windows / C# | MIT | 微软官方运行时基类库 (`System.Security.Cryptography`, `System.Net.Sockets`) |

## 4. vivo 开源声明的借鉴边界

本次审查 vivo 办公套件公开的依赖清单后，评估过 `file-type`、`mediainfo.js`/MediaInfo、`ExifReader`、`fflate`、`pako` 和 `Jimp`。它们没有被直接复制、打包或加入 Hinge 的依赖锁文件：

- 文件头/MIME 识别由 `android/android/app` 内的小型 Kotlin 实现完成；
- 音视频参数使用 Android Framework 的 `MediaMetadataRetriever`；
- 图片尺寸使用 `BitmapFactory`，API 24 及以上的 EXIF 使用 Android Framework `android.media.ExifInterface`；
- Windows 继续使用 WinRT `Launcher`、文件属性和 .NET BCL，不新增媒体 JavaScript/WASM 运行时。

因此，这次功能改动没有新增第三方许可证或 NOTICE 文件。上面这些项目只是候选方案和实现思路的对照，不应被误写成 Hinge 的运行时依赖。真正随 Hinge 发布的依赖必须同时登记在 `THIRD_PARTY_LICENSES.md`。

## 5. Electron 托盘兼容实现参考

Windows 端 QQ/微信托盘唤醒参考 Electron 项目（MIT License）的公开实现：`shell/browser/ui/win/notify_icon_host.cc` 和 `notify_icon.cc`。Hinge 只依据其公开消息约定识别 `Electron_NotifyIconHostWindow`、`WM_APP + 1` 回调与通知图标 ID，并结合 Windows 原生 `Shell_NotifyIconGetRect`/`PostMessage` 完成兼容；没有复制、链接或打包 Electron 源码和二进制，因此不新增运行时依赖或许可证文件。

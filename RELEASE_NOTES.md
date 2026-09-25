# Hinge v1.4.0

本版以 GitHub 上一个公开稳定版 [`v1.2.4`](https://github.com/Chengeeker/Hinge/releases/tag/v1.2.4) 为对照。重点是让 Android 用户能看见 Cloud Relay 文件传输的实际进度，并在应用内追踪 LAN 与 Relay 文件收发记录。

> Cloud Relay 文件中转并非本版从零新增：`v1.2.4` 源码已经包含可选的 Cloudflare Worker + R2 文件中转。本版新增/完善的是配置持久化、分段内实时进度、通知状态和 Android 传输历史。Relay 仍由用户自行部署，不是 Hinge 官方云服务。

## Cloud Relay 文件中转体验

- Android 上传和下载时，通知中心持续显示传输方向、当前阶段、已传输字节和百分比。进度涵盖准备、网络传输、完整性校验及云端提交，不再只在每个 8 MiB 文件分块完成时跳动。
- 上传成功后通知明确显示“已上传，等待对方接收”；下载进度结束后交由现有“收到文件”通知提示。失败时保留可查看的状态并显示 HTTP 错误码（如适用）。需允许 Hinge 发送通知；系统彻底停止 Hinge 时，不保证立即轮询或下载。
- 修复 Android 重启应用后 Cloud Relay 开关、Worker 地址、设备 Token 和 Relay 加密密钥恢复为空的问题。部署管理员 Token 只用于设备注册，不作为客户端保存项。
- 使用自己的 Cloudflare Worker 与私有 R2；网页版部署教程、Worker 项目及后续更新入口见 [Hinge-Relay 项目主页](https://github.com/Chengeeker/Hinge-Relay)。客户端 LAN 会话可用时仍优先直连；Relay 只面向 Hinge 中已信任的设备。

## 文件传输记录与连接状态

- Android “设备操作”中的“文件管理”现改为“文件传输记录”。本机保存最近 100 条 LAN/Cloud Relay 收发记录，包括方向、设备、状态、时间和活动传输进度；可单条删除或清理已结束记录，不会因此删除收到的实际文件。
- Android 原生待发送队列的任务也会显示在历史中；自动重试会保留为等待/重试状态。当前原生队列没有取消接口，所以记录页不显示无法工作的取消按钮。
- Android 断开设备时会关闭该设备的全部活动会话，包括页面启动前已恢复的会话；页面也会为已恢复和新建立的会话接入文件传输事件。
- Android 连接失败提示不再遮住自定义悬浮导航胶囊，提示期间仍可操作导航。

## Android 主题和剪贴板变更

- 预置颜色方案改为标准 Material 3 `tonalSpot`。Android 壁纸动态颜色（Monet）与独立的 Expressive 胶囊导航保留。
- 移除 Android/Windows 跨设备剪贴板同步和 Android Shizuku 后台剪贴板监听，包括 LAN 帧与客户端 Cloud Relay 剪贴板调用。主动把验证码、Relay 密钥等复制到本机系统剪贴板仍可使用。
- Cloud Relay 现仅用于文件中转。已部署的 Worker 独立于客户端发行，不会随本次 Hinge 更新自动部署；Worker 代码/已部署接口的变更需在 Relay 项目中单独更新和部署。

## 安装包与验证

| 平台 | 资产 | 版本/架构 | 大小 | SHA-256 |
| --- | --- | --- | ---: | --- |
| Android | [`Hinge.apk`](https://github.com/Chengeeker/Hinge/releases/download/v1.4.0/Hinge.apk) | `1.4.0+99` · `arm64-v8a` | 21,159,073 bytes | `0C332B8DE685C0C20B8438F853CDFA5CF4BB1A33F6DEE28665FD22A004D42168` |
| Windows | [`Hinge-Setup.exe`](https://github.com/Chengeeker/Hinge/releases/download/v1.4.0/Hinge-Setup.exe) | `1.4.0.0` | 198,105,656 bytes | `5942E3A7509D78CAB0741EBB9F8D9A2D0B45ED0174F6B63F3D47E31809FFEEF6` |
| Windows | [`Hinge-Windows.zip`](https://github.com/Chengeeker/Hinge/releases/download/v1.4.0/Hinge-Windows.zip) | `1.4.0.0` | 129,723,669 bytes | `682842548F810449C2690E7838583EA2390CFC93DC1B7ABE195CF540EE1A827E` |

验证结果：Android `flutter analyze --no-pub` 无问题、Flutter 测试 62/62 通过；APK 包名 `com.hinge.office`、versionCode `99`、仅含 `arm64-v8a`，APK v2/v3 验签通过且稳定签名证书一致。Windows .NET 测试 94/94 通过，Release 安装器/ZIP 构建成功，安装器文件版本与 ZIP 内稀疏身份清单均为 `1.4.0.0`。Windows 测试期间 NuGet 漏洞索引返回 `NU1900` 网络警告，不影响构建和测试。

尚未在用户 Android 真机上完成通知中心进度、跨网络 Cloud Relay 文件端到端收发或不同厂商后台策略验收。自动化分析、测试和构建结果不等于所有设备及 Cloudflare 部署均已实机验证。

升级后 Cloud Relay 两端仍须使用相同 Relay 加密密钥，并分别完成设备注册。Hinge 不托管 Worker，也不会自动改动用户的 Cloudflare 配置或部署。

# Hinge 代码与文档真实性审计报告

审计日期：2026-09-05  
审计基线：`main` / `0aeef8b` (`fix(windows): fix startup null reference exception and add crash logging in WPF app`)；本轮将桌面入口切换为与 Android 共用的 Flutter Material 3 Expressive 工作台，并补充 Android 原生数据桥。
审计范围：仓库结构、协议 SSOT、Windows Core/Platform/App/Tests、Android Flutter 代码与测试、构建脚本、项目文档。

## 1. 审计结论

当前仓库不是 Phase 0 空骨架，而是已经包含设备发现、配对模型、TCP 会话、文本/文件传输、剪贴板、同步算法、遥控、投屏 Mock 管线、通知/工具 Mock 以及双端工作台的多阶段原型。

但它仍不能被定性为“Android 与 Windows 的真实生产产品”或“全功能已完成”：

- Windows `.NET` Core/WinUI 解决方案仍可作为协议基线验证：构建 0 警告/0 错误、xUnit 52/52 通过；新的 Flutter Windows 宿主已使用 Visual Studio Build Tools C++ 工具链生成 release bundle。
- Android release APK 已通过 `scripts/build_release_android.ps1` 生成，包含共享 Flutter UI、Material Symbols Rounded、原生数据桥和双端配对命令路由。
- Android 测试源码包含 45 个测试，但本轮使用项目已有的 `D:\flutter_sdk\bin\flutter.bat` 调用时超过 90 秒无输出，已停止；本轮不能确认 Android 测试通过。
- 真实配对认证、会话加密、跨端 UUID 兼容、Android 原生投屏/通知桥接、Windows H.264 解码均未形成可验证的真实闭环。
- 若不先修复协议与安全边界，当前“测试通过”只代表单端或 Mock/回环行为通过，不能代表跨设备可用。

审计阶段先按交接文档完成真实性核验；用户随后要求继续推进，本轮在保留协议基线的前提下完成共享 Flutter 工作台和 Android 数据接口垂直切片。

## 1.1 Sprint 2 协议基线进展（2026-09-05）

在用户确认后，已完成第一组 P0 协议修复：

- C# 帧头和文件块 TransferId 统一改为 RFC 4122 网络字节序；
- Dart 帧默认生成随机 v4 MessageId，SessionId 保持未建会话时的全零语义；
- C# 与 Dart 的 PayloadLength 改为 uint32 读取/写入，并在分配前拒绝超过 16 MiB 的载荷；
- Session TCP 读取循环复用同一长度上限；
- 增加 C# RFC 4122 golden vector、长度拒绝测试，并同步协议文档。

修复后的历史验证结果：Windows 构建 0 警告/0 错误、xUnit 52/52 通过；Dart SDK analyzer 0 issues。4.3 节的真实鉴权/加密和配对模型缺口仍未修复。

## 1.2 双端 UI 垂直切片进展（2026-09-05）

- Android 与 Windows 现在共用 `android/` Flutter 工程和 Material 3 Expressive 工作台；桌面使用侧边导航，手机使用抽屉导航，导航顺序固定为首页、已连接的机型、连接设备、笔记代办、日历、相册、设置。
- Android `MainActivity` 已通过 MethodChannel 接入 `StatFs`、`CalendarContract`、`MediaStore` 和 `SharedPreferences`；Windows 端通过 `TOOL_COMMAND/TOOL_RESULT` 请求手机存储、日历、相册、文件列表和图片缩略图。
- 笔记与待办已经支持新建、编辑、完成、删除和持久化；Windows 桌面端的身份、信任和下载路径对齐旧 WinUI 客户端的 `LocalAppData\\Hinge`，已有配对记录不会因更换 UI 丢失。
- 投屏仍然只显示真实的未接入状态，不显示 Mock 画面；真实会话加密、投屏视频管线、通知桥和 OCR Provider 仍未形成闭环。

## 1.3 发布前稳定性复审（2026-09-06）

本次发布前复审已把上一版文档中“Windows WPF”“Flutter Windows 宿主”和 Android 测试未完成等过时描述与当前工程分开：Windows 当前入口是 .NET 8 + Windows App SDK + WinUI 3，Android 测试和静态分析均已重新执行。

- Windows 会话在身份握手完成后才对功能页报告可用；文件管理请求统一经过当前会话调度器，避免首页连接状态与功能页连接对象分裂。
- Android 会话输出串行化，并对后台保活使用 `connectedDevice` 前台服务；这只能降低系统回收概率，不能替代各厂商的省电白名单设置。
- 文件管理使用 200 条首批加载和后续分页，缩略图按预算读取；Windows 图片、视频、音频交给系统默认关联应用。
- Android Monet 动态取色读取系统 `system_*` 颜色角色，关闭时使用预置主题。
- 本轮验证：`flutter analyze` 通过、Flutter 测试 47/47 通过、Windows xUnit 53/53 通过、Windows Release 编译通过；APK V2/V3 签名验证通过。

完整的当前状态、产物路径和真机验收边界见 `docs/development-status.md`。

## 2. 文档指令与用户请求的区分

本轮将两份外部 Markdown 文档中的内容分为两类处理：

1. 作为项目约束执行：LAN-first、Native-first、MIT First、先检查现有实现/原生 API/现成库、协议 SSOT、先审计再进入 Sprint 2，以及不得把 Mock/单测冒充真机完成。
2. 作为项目状态或计划进行核验：文档中的“已完成”“L5”“Phase 0”“WinUI 3”“测试全通过”等均没有直接当作事实，而是与当前仓库和本轮命令结果交叉检查。

因此，“直接开始工作”在本轮对应交接文档规定的第一项实际工作：完成代码与文档真实性审计并产出本报告，而不是立即追加业务功能。

## 3. 已核对且基本一致的部分（Aligned）

### 3.1 仓库与依赖

- Windows 解决方案实际包含 `Hinge.Core`、`Hinge.Platform`、`Hinge.App` 和 `Hinge.Tests`；App 现为 `net8.0-windows10.0.19041.0` + `UseWinUI=true` + `Microsoft.WindowsAppSDK 2.4.0`。
- `Hinge.Core` 和 `Hinge.Platform` 没有生产 NuGet 引用；测试工程的 xUnit 等包仅用于测试。
- Android `pubspec.yaml` 使用 Flutter SDK、`crypto`、`cupertino_icons` 和 `material_symbols_icons`；网络、媒体和本地数据访问仍由自研 Dart 核心与 Android 原生桥完成。
- `core/` 下有规划用空目录，但目前没有可编译的共享源文件；实际业务实现位于 Windows Core 和 `android/lib/core`。

### 3.2 发现与状态注册

- 两端都实现 UDP 52830 广播/单播发现，发送端会同时尝试全局广播和各活动 IPv4 网卡的定向广播，TCP 端口默认为 52831，并携带设备名称、平台、能力和协议版本。
- 两端注册表都支持设备重新出现和约 10 秒无消息后的离线降级。
- 两端都有手动 IP 探测入口。

### 3.3 基础二进制字段

- `MessageType` 的主要枚举值在 C# `HingeProtocol.cs` 与 Dart `protocol_frame.dart` 中一致。
- Magic 为 `OSP1`，版本和整数型字段按大端序写入。
- 文件块实现使用 64 KiB；文件子头实际为 16B TransferId + 4B ChunkIndex + 8B Offset，共 28B。
- 遥控事件实际为 16B 固定头，文本输入追加 UTF-8。
- 投屏模型实际使用 20B 子头，字段顺序与 `protocol/protocol.md` 的 20B 定义一致。

### 3.4 已验证的测试边界

- Windows `dotnet build windows\\Hinge.sln --no-restore`：通过，0 警告、0 错误。
- Windows `dotnet test windows\\Hinge.sln --no-restore`：52/52 通过。
- Windows 测试覆盖了协议回环、发现、会话回环、文件回环、哈希、同步算法、遥控 Mock、投屏 Mock、通知过滤和 Mock OCR；这些属于单端/回环/Mock 证据。

## 4. 需要阻断继续扩展的差异（Discrepancies）

以下项目优先级按“会导致跨端失败或造成未授权能力”排序。

其中 4.1 和 4.2 的协议编码问题已在 1.1 节关闭；本节仍保留原始审计证据，并继续记录尚未解决的认证、加密和业务边界问题。

### P0 — 协议兼容性与未认证会话

#### 4.1 UUID 二进制字节序不符合 SSOT，文件传输跨端 ID 会错位（已关闭）

- 协议要求 UUID 使用 RFC 4122 网络字节序；本轮已在 C# 帧、C# 文件块和 Dart 帧实现中统一，并加入 golden vector。
- Dart 帧默认生成随机 v4 MessageId，未建立会话时仅 SessionId 保持全零语义。

#### 4.2 帧长度是有符号 int32，且没有安全上限（已关闭）

- `protocol/protocol.md` 定义 Payload Length 为 uint32；本轮 C# 和 Dart 均已改用 uint32 读写，并在分配前拒绝超过 16 MiB 的载荷。

#### 4.3 TCP 连接没有真正的 TrustStore 鉴权或加密

- `SessionConnection` 初始状态直接是 `Connected`（`windows/Hinge.Core/SessionManager.cs:39-40`；Dart `session_manager.dart:29`）。
- `SessionManager` 的 `_trustStore` 没有参与入站/出站握手；`SESSION_INIT`、`SESSION_ACK` 在代码中只有枚举，没有实际握手处理。
- 未发现 `SslStream`、AEAD、ECDH/X25519、签名验证或会话密钥派生实现。当前业务帧在 TCP 上明文发送。
- `RemoteDeviceId` 只是可写属性（C# `SessionManager.cs:41`），当前没有从握手报文中可靠填充。
- Remote、Notification、Screen 的“信任检查”都在 `RemoteDeviceId` 为空时跳过（例如 `RemoteInputManager.cs:25-37`、`NotificationManager.cs:58-65`、`ScreenStreamReceiver.cs:71-78`）。测试还显式以 `null` connection 作为绕过路径。Clipboard 与 Transfer 的入站处理本身也没有 TrustStore 检查。
- 结果是：监听到 TCP 端口的未配对节点可以建立连接并发送业务帧；在 `RemoteDeviceId` 为空时，遥控和投屏检查不会阻断。

### P0 — 配对模型仍不是文档规定的安全配对

- `PairingManager` 仍保留并被 UI 使用旧的 `DerivePin(..., "pair_salt_default")` 回退（C# `PairingManager.cs:70-79`、Android `app.dart` 的 `_showPairDialog`）。这正是交接文档要求废止的确定性 PIN。
- 当前 `CreatePairConfirm` 的默认 shared secret 是 `SHA256(deviceIdA:deviceIdB)`（`PairingManager.cs:138-149`；Dart 同逻辑），不是 ECDH/X25519 共享秘密。
- `DeviceIdentityManager` 只随机生成 32 字节并把它标成 `publicKey`（C# `DeviceIdentityManager.cs:63-70`；Dart `device_identity_manager.dart:63-75`），没有私钥、公钥算法、签名、指纹校验或 Keystore/DPAPI 保护。
- 配对代码只生成消息对象和本地弹窗，不通过会话发送 `PAIR_REQUEST`/`PAIR_CONFIRM`，没有双方状态机、SAS 验证函数、签名证明或协议规定的 ACK/Verified 步骤；用户点击确认后直接 `SaveTrustedPeer`。

### P1 — 文件传输违反流式与断点安全边界

- Windows 端的哈希计算是流式的，但 Dart `sendFile` 在发送前调用 `file.readAsBytes()`（`android/lib/core/transfer_manager.dart:87`），接收完成后又对整个 `.part` 文件调用 `readAsBytes()`（约 `:292`），与“内存零压力”要求不符。
- Dart 接收端以 append 模式打开文件并忽略 ChunkIndex/Offset（`transfer_manager.dart` 的 `_handleFileChunk`）；乱序、重复或错误 offset 不会按协议定位写入。
- 两端对 `accept.offset` 没有严格验证，也没有统一校验 chunk index、单块长度、累计长度和 `FILE_COMPLETE.success`；C# 收包侧同样缺少这些边界检查。
- Sync 的 `SYNC_PULL_REQ` 将外部相对路径直接 `Path.Combine` 到同步目录（`windows/Hinge.Core/SyncManager.cs:80-87`），没有确认规范化后的最终路径仍在根目录内，存在路径穿越风险。

### P1 — 心跳和重连不满足协议描述

- 文档要求连续 3 次/15 秒无应答进入重连；C# 和 Dart 使用 `missed > 3`，实际至少到第 4 次计数才迁移。
- 迁移到 `Reconnecting` 后只停止循环，没有实现文档所说的 1、2、4、10 秒指数退避和自动重连；也没有连接级重连事务或 MessageId 保留策略。
- C# 多个异步发送路径直接写同一个 `NetworkStream`，没有发送队列/串行写保护；心跳响应、业务帧和回调发送可能并发写入。

### P1 — 投屏、通知、OCR 仍是 Mock 或占位实现

- Android `MainActivity` 已接入存储、日历、相册、文件和工作区持久化的 MethodChannel；Manifest 已声明日历与媒体读取权限，但仍没有 MediaProjection 或 NotificationListenerService 的真实接入。
- Android 默认 `ScreenStreamManager` 使用 `MockScreenStreamCapturer`；它产生合成 SPS/PPS/IDR/P 帧，不代表真实屏幕编码。
- Windows `Win32ScreenRenderer.RenderFrame` 只解析 NAL 类型并递增计数（`Win32ScreenRenderer.cs:26-54`），没有 Media Foundation/Direct3D/DXVA 解码和真实画面窗口。
- 迁移前 WPF/WinUI 的“请求关键帧”按钮只更新状态文字（历史 `MainWindow.xaml.cs:671-674`），没有发送投屏控制帧；当前 Flutter 工作台同样明确标注投屏链路尚未接入。
- Android/Windows OCR 默认均为 `MockOcrEngine`，返回硬编码文本；没有真实 Windows OCR 或 Android 离线 OCR Provider。
- Windows 通知 presenter 的实现是内存列表加 `Console.WriteLine`，不是 Windows Toast/Action Center；Android 没有通知监听服务。

### P2 — 同步与垂直切片尚未达到功能完成

- `SyncManager.OnDirectoryChanged` 为空（`windows/Hinge.Core/SyncManager.cs:37-40`）；当前是清单/差异算法，不是完成的 Backup/One-way Sync/Two-way Sync 产品链路。
- `tests/integration` 与 `tests/protocol` 当前没有可执行测试文件；现有“集成”主要是各端测试工程中的 localhost 回环。
- 旧审计发现的 UI/发布文案漂移已在本轮同步：Windows UI 使用协议常量对应的设备发现服务，分块文案统一为 64 KiB。

## 5. 文档与仓库状态漂移

### 5.1 产品技术栈名称不一致

- 根目录 `windows/` 保留 .NET Core/WinUI 代码和测试基线；实际新的 Android/Windows 客户端入口是 `android/` 下的 Flutter 工程，桌面构建使用 `flutter build windows`。
- 历史 WinUI/WPF 记录只用于说明迁移前状态；新的 Windows package 已由 `publish/windows/Hinge-Windows.zip` 交付，包内另附防火墙配置助手。

### 5.2 协议 SSOT 文件清单不一致

交接文档列出了 `protocol/framing.md` 和 `protocol/remote_input.md`，但当前仓库没有这两个文件；协议内容实际散落在 `protocol/protocol.md` 和各专题文件中。应在下一轮明确唯一文件，避免继续引用不存在的规范。

另外，`protocol/screen_stream.md:6` 写“固定 16 字节”，同文件后面的结构和代码都是 20 字节；该协议文档仍需以 SSOT 做一次清理。

### 5.3 测试与发布数字过期

- 当前 Windows 测试运行结果为 52 个通过，Dart analyzer 为 0 issues；Android release APK 已成功构建。Android Flutter wrapper 测试命令在本机仍出现长时间无输出，因此不宣称测试套件已重新通过。
- 交接文档的 95/50/45 数字与源码规模一致；本轮 Android release 构建已验证通过，但 wrapper 测试命令仍因本机长时间无输出未确认。
- `scripts/test_all.ps1` 和 `scripts/build_release_android.ps1` 硬编码 `D:\flutter_sdk\bin\flutter.bat`；该文件存在，release 构建入口可用，测试入口仍需单独排查无输出问题。

### 5.4 文档文件被污染

`docs/compatibility.md` 的前半段是编码规范，后半段混入了 PowerShell `Out-File` 命令文本和兼容性表；仓库没有对应的 `docs/coding_rules.md`。这不是有效的兼容性矩阵，应在修改功能前恢复为单一、可渲染的文档。

## 6. L4 → L5 真机缺口清单

当前最重要的真实验证缺口为：

1. Android 真机与 Windows PC 的真实 UDP 发现、多网卡、休眠、网络切换和防火墙场景。
2. 双端真实交互式配对、信任持久化、重启后签名会话和未授权连接拒绝。
3. Android↔Windows 真实文本、64KiB 文件块、Unicode 文件名、大文件、取消、断点、哈希错误和磁盘不足。
4. Android 10+ 前后台剪贴板限制，以及 Pixel/Samsung/Xiaomi/OPPO/vivo 的后台策略。
5. Android MediaProjection→MediaCodec→网络发送和 Windows 解码渲染的完整链路。
6. Android NotificationListenerService 到 Windows 展示/回复的完整链路。
7. 真实 OCR Provider、权限申请、失败降级和隐私日志检查。
8. 长连接断网、重连、重复帧、畸形长度、重放、并发发送和资源释放。

## 7. 后续真实产品化顺序

1. 协议金标准的 UUID 网络字节序、uint32 PayloadLength、最大帧大小和 C#↔Dart golden vectors 已完成；继续补未知版本/类型行为。
2. 先实现真实身份与会话边界：长期密钥存储、交互式 SAS、签名握手、会话密钥、TrustStore 强制拒绝路径；删除 UI 的旧 PIN 回退。
3. 再修复 Transfer：流式哈希、严格 offset/index/长度/路径校验、错误 ACK、取消与恢复，并用跨端帧向量和真实双进程测试验证。
4. 修复心跳/重连和发送串行化，补上 `IStreamTransport` 抽象或明确其最终落点。
5. 之后再接 Android 原生投屏/通知与 Windows 真正解码/Toast；Mock 继续保留为单测 Provider，但 UI 文案必须明确标注 Mock/未接入。
6. 最后校准 README、roadmap、architecture、compatibility、release notes 和脚本，避免文档再次高于代码事实。

下一步应优先完成真实身份/会话安全和跨端真机验证；UI 垂直切片可以继续完善交互，但不得把当前占位入口包装为真实投屏、通知或 OCR 已完成。

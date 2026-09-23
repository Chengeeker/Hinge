# Changelog

## [1.2.4] - 2026-09-22

> 本版移除 Android 接收端逐块哈希这一项已观察到的吞吐负担。A/B 测试曾明显改善速度，但后续同体积 APK 复测仍出现快慢差异，因此本版属于性能缓解，不把它描述为全部根因已经解决。

### 文件传输性能

- Android 接收文件时不再在 Flutter isolate 中对每个 2 MiB `FILE_CHUNK` 同步计算 SHA-256；文件仍会在接收完成后以流式方式校验摘要，数据完整性校验没有取消。
- 保留 Windows 右键菜单发送、现有 LAN TCP 协议、2 MiB 分块、Windows read-ahead、直接组帧和 Cloud Relay 可选路由。
- 同一文件的 A/B 真机对比曾明显改善速度，正式包因此沿用该接收校验策略；后续复测表明仍需区分断点续传偏移、Android 实际接收路径和真实 TCP 吞吐。
- 2026-09-23 的接收端日志中，四次约 198 MB 的从头传输耗时 `2.49–3.56 s`（约 `53–76 MiB/s`）；本轮没有复现此前的慢速样本。对应 Windows 分段计时缺失，因此该数据验证当前设备可达到的吞吐，不证明慢速问题已在所有状态下根治。

### 版本

- Android：`1.2.4+83`。
- Windows：`1.2.4.0`。
- Android APK：`21,142,689` bytes，SHA-256 `EA47E85EFC79B0D989D3309E4908D02BB324BACB6CC1DB64E6AE68DA2D10B495`；已完成稳定签名、`versionCode=83` 和 `arm64-v8a` 校验。
- Windows 安装器：`198,110,458` bytes，SHA-256 `B3B99DCDC04C4E9840B4651A72B3C7330DC772BD0C8A53E58DED6B254F6301A3`。
- Windows 便携包：`129,728,474` bytes，SHA-256 `240D2C3878701FC32E995AD3538EBF088D67C06F245146A683BCC884E83D059B`。

## [1.2.1] - 2026-09-22

> 本版是针对上一版实测传输吞吐、进度显示和后台路径的兼容式性能修正版。没有更换 Hinge 现有的 LAN TCP 帧协议，也没有把 BLE 改成数据通道；新优化通过会话 capability 协商启用，旧版对端继续使用原有校验和分块兼容路径。实际速度仍需结合目标 Wi-Fi、手机存储和 OEM 电源策略在真机上验收。

### 文件传输性能

- **跳过新版本双方之间不必要的传输前整文件预扫描**：新增 `streaming-file-hash-v1` 会话能力。双方均支持时，新的文件传输在 `FILE_OFFER` 中先发送空摘要，发送端边读取边计算 SHA-256，最终在 `FILE_COMPLETE` 中提交完整摘要；这样不会为了生成 offer 摘要先把 21 MB、数百 MB 甚至更大的文件完整读取一遍，再从文件开头读取第二遍。
- **保留旧版兼容性**：如果对端没有声明新能力，Windows、Android 原生 broker 和 Dart 备用路径仍在发送 `FILE_OFFER` 前计算完整 SHA-256；旧版接收端不需要升级即可继续收发，旧版/新版混连不会因为空摘要改变校验语义。
- **Windows 发送端加入有界读写流水线**：文件读取使用 3 个 2 MiB 缓冲块做有限 read-ahead，磁盘读取和网络写入可以重叠；队列有明确上限，不会因为大文件或慢手机无限制堆积内存，取消、断线和异常时会取消生产者并归还缓冲区。
- **Android 原生发送队列加入同样的有限 read-ahead**：原生 broker 使用 3 个 2 MiB 缓冲块，并在原生 I/O 执行器中完成读取、校验和和发送，后台发送不依赖 Flutter isolate 的调度时机。
- **减少大块内存复制**：Windows `FILE_CHUNK` 直接把传输头和文件数据写入从 `ArrayPool` 租用的最终帧；Android 原生 broker 直接组装最终 socket 帧；Dart 备用路径也不再先构造 28 字节文件块 payload 再复制到完整帧中。
- **优化 Windows 接收端**：socket 读取时将固定头和 payload 分开读取并直接复用 payload，不再为了解析再复制一份完整 2 MiB 帧；连续接收的新文件边写入边校验，避免传输结束后再次完整读取临时文件。
- **优化 Android/Dart 接收校验**：Android 原生 broker 和 Dart 备用接收路径对从零开始、连续到达的文件块边写入边计算摘要；续传或出现偏移不连续时自动回退到完整文件流式校验，优先保证正确性。
- **保留 2 MiB 分块和不逐块 flush 的既有优化**：协议仍兼容旧版 64 KiB/512 KiB 等小分块，控制帧继续及时 flush，文件块不会逐帧等待无意义的 flush。
- **增加实际吞吐诊断**：Windows 传输状态栏会显示当前百分比和测得的有效字节速率；Android 原生连接诊断记录完成耗时、字节数和 bytes/second，便于区分协议开销、手机写盘和 Wi-Fi 链路瓶颈。

### 进度与可靠性

- **传输进度继续按 UI 节流刷新**：网络块可以连续发送，但 Windows 首页仍按约 100 ms 合并历史写入和界面重绘；后台传输不会因为每个 2 MiB 块都落盘而降低吞吐。
- **保留取消、断线重试和续传**：流水线取消时会同时停止文件读取和 socket 写入，待发送队列和已有取消按钮语义不变；续传请求仍使用接收端返回的 offset，并在需要时使用完整文件摘要校验。
- **不会强制启用高风险独立 Bulk 通道或 HTTP 协议重构**：本版只改现有协议的帧组装、缓冲和校验路径，避免改变文件队列、取消、历史记录和旧版本互操作边界；独立 Bulk/HTTP 通道可作为后续有单独协议测试和真机基准后的版本工作。

### 版本与验证

- Android：`1.2.1+80`，包名 `com.hinge.office`，目标架构 `arm64-v8a`。
- Windows：`1.2.1.0`，提供自包含 EXE 安装器和便携 ZIP。
- 本轮完成 Dart 静态分析、Flutter 全量测试、Windows Release 编译/测试和 Android Kotlin/Release 编译校验后再生成最终双端安装包；Dart 分析无问题，Flutter 全量测试 `64` 项通过，Windows .NET 测试 `102` 项通过。测试阶段仅有既有 NuGet 漏洞数据源不可达的 `NU1900` 警告。
- Android APK：`20,815,009` bytes，SHA-256 `5CBD31D4762873049C99D13398571FBA2A916C9F8D3E212393F53C1E963FE9FF`；发布脚本已完成稳定签名和 ARM64 架构校验。
- Windows 安装器：`198,075,383` bytes，SHA-256 `22D83B4C9FE6F704350C1FD454E5BDE73C89C3B0009513A7ABFF135F6AB52E33`；已核验包含 `HINGE_PAYLOAD_V1`。
- Windows 便携 ZIP：`129,693,402` bytes，SHA-256 `B1D2A7DF16C72327A72BF5387613DCDFE546B8C5EA9A0159C51989B3AF2A2EAD`；已核验包含 `Hinge.exe`、`Hinge.Identity.msix` 和 `Hinge.ShellExtension.v1.2.1.dll`，稀疏身份包版本为 `1.2.1.0`。
- 真机最终验收应同时测试小文件、约 20 MB 文件、数百 MB 文件、多个文件、取消、断线续传，以及 Android 锁屏/原生服务路径；本版不会把自动化回环测试写成所有 vivo/其他 OEM 已验证。

## [1.2.0] - 2026-09-21

> 本版基于 GitHub 上一版正式发行版 `v1.1.35` 的发布内容，汇总其后已经实现并完成双端 Release 构建的 Android 后台连接、低功耗待命、BLE 按需唤醒、Windows 首页、传输体验和界面修正。以下只描述当前源码和发布包实际包含的能力，不把真机尚未验证的 OEM 行为写成保证。

### Android 后台连接与低功耗待命

- **修复息屏静止约 180 秒后被误判离线**：Android 原生 broker 不再把后台无入站帧直接视为连接死亡，而是进入 `Suspended` 状态，保留已认证 socket、服务所有权和后台探测；收到任意合法协议帧后恢复为 `Connected`。真实 socket 读写错误、FIN/RST、协议错误和网络回调仍会进入断线/重连路径。
- **统一 Windows 与 Android 的后台心跳语义**：Windows 识别 Android 对端后使用 20 秒心跳，连续静默只进入 `Suspended`，不再仅因 PONG 计数过期就主动销毁 TCP；任意合法协议帧都会刷新存活状态。Android 息屏恢复后启动 1 秒间隔、最长 15 秒的恢复探测，确认仍不可达后才重连。
- **增加原生重连退避**：历史设备重连采用 2 秒、5 秒、10 秒、30 秒、60 秒退避；成功建链或网络切换后清除退避计数，避免 Doze、VPN 或网络切换期间反复高频连接。
- **扩展可用状态模型**：Flutter、Windows、Android/Windows 设备注册表和工作区入口统一识别 `Suspended` 为“已认证但暂时休眠、可恢复”的会话；发现包短暂丢失时不会把仍由 TCP 持有的设备清成离线。
- **改为低功耗待命目标**：Android 前台服务在离开前台、屏幕熄灭且活跃窗口结束后，停止主动发现信标循环，释放 CPU/Wi‑Fi/组播锁并暂停周期心跳；常驻通知继续保留，设备需要通信时再恢复连接窗口。
- **更新常驻通知和保活设置文案**：活跃阶段说明后台服务正在运行，待命阶段说明低功耗待命和唤醒入口，不再暗示普通第三方应用能够永久保持一条 TCP 连接。

### BLE 按需唤醒与 Windows 右键发送

- **新增 Windows → Android 的 BLE 唤醒通道**：Windows 右键发送文件时广播短时 manufacturer-data 唤醒包，只携带版本、唤醒原因和设备标签，不携带文件内容、配对码或局域网凭据；BLE 只负责唤醒，不承担文件传输。
- **支持 Android Companion Device 关联**：Android 新增 Companion presence service 和关联管理，系统检测到已关联的 Windows 唤醒广播后启动已有前台服务，并请求原生 broker 立即恢复历史可信设备连接。
- **保留通知兜底**：BLE 不可用、Windows 没有适配器、设备尚未关联或厂商策略阻止自动唤醒时，Windows 右键发送仍会把文件写入持久化待发送队列，并提示用户点击 Android 常驻通知；连接恢复后继续自动发送，不丢文件。
- **补充低功耗唤醒生命周期**：待命期间即使收到新的 LAN 握手，也不会立即在后台消费大文件队列；只有唤醒窗口恢复后才分发排队传输，避免手机无感启动大文件传输。
- **新增配对和能力设置**：Android 保活设置页提供 Companion 绑定、解除绑定和能力状态；Windows 设置页和托盘提供 BLE 配对广播入口。BLE、厂商省电策略、强行停止应用和真实设备唤醒仍需目标设备验收，不能保证所有 Android OEM 都放行。

### Windows 首页与传输记录

- **重做首页设备与传输区域**：移除可见的“连接状态”卡片和顶部“发送文字”按钮，保留后台生命周期所需的隐藏状态控件；首页聚焦设备发现、连接操作、剪贴板同步和文件传输。
- **新增本地传输记录**：普通发送、拖放发送、Explorer 右键发送和 Windows 接收文件都会记录到本地 `Hinge/transfer-history.json`，最新记录在上方，记录区块独立滚动，不再撑满整个首页。
- **支持未连接时的待发送状态**：Explorer 右键发送在手机未连接时先写入 `pending-file-sends.json`，首页显示“待发送 · 等待设备连接”；连接或唤醒成功后继续发送，多文件任务可整体取消。
- **支持单条删除和清空记录**：已完成、失败、已取消和其他非进行中记录可以单条删除，也可以通过“清空记录”批量删除；清空前确认。进行中的发送、等待连接、建立发送和校验任务不会被误删，实际文件和持久化发送队列也不会被删除。
- **修复首页传输进度跳变**：传输记录复用已有 `TransferProgress` 回调，以约 100 ms 节流刷新本地记录，并在活动记录中显示实时进度条，不再只从 0% 跳到 100%；取消或失败时保留已传输字节数。
- **优化文件传输开销**：Windows、Android 原生 broker 和 Flutter 备用路径统一使用 2 MiB 发送分块；文件块不再逐帧 flush socket，Windows 文件读取启用顺序异步读取，接收端仍兼容旧版小分块。实际吞吐仍取决于 Wi‑Fi 链路、手机写盘和 SHA-256 预扫描。

### 剪贴板与连接状态核查

- **确认剪贴板同步写入系统剪贴板**：Android 收到剪贴板事件后调用系统 `Clipboard.setData`，Windows 收到事件后使用 Win32 `EmptyClipboard` 和 `SetClipboardData(CF_UNICODETEXT, ...)`；不是只把文本显示在 Hinge 页面中。
- **保留回环抑制**：两端继续使用来源内容缓存和最近事件缓存，避免远端写入系统剪贴板后又被本端监控重复转发。
- **修复 Android 首页连接按钮状态**：以认证会话和 `connectionForDevice(...).isReady` 为最终依据；已连接设备显示“断开连接”，未连接设备显示“连接”，同步覆盖连接设备页和首页设备卡片。

### Windows 界面与系统集成修正

- 修复 Windows 任务栏、窗口和托盘可能显示灰色占位图标的问题，统一优先从多尺寸 `Assets/app_icon.ico` 加载；桌面和开始菜单快捷方式也引用同一份 ICO，不重新引入会导致跨容器问题的 AUMID 方案。
- 修复主页“重新搜索”、附近设备“断开连接”、通知历史筛选/排序/清空/刷新按钮的高度、基线和动作列对齐。
- 修复附近设备默认 `ListViewItem` 悬停背景覆盖整行的问题，让悬停背景不再盖住右侧“断开连接”按钮区域。
- 保留 Windows 11 第一层 Explorer 右键菜单、签名稀疏身份包和便携版兼容菜单边界；v1.2.0 安装器与便携包中的 Shell 扩展文件名按版本隔离，避免 Explorer 占用旧 DLL 时升级覆盖失败。

### 版本与验证

- Android：`1.2.0+79`，包名 `com.hinge.office`，目标架构 `arm64-v8a`。
- Windows：`1.2.0.0`，提供自包含 EXE 安装器和便携 ZIP。
- Dart 静态分析通过；Flutter 全量测试 64 项通过；Windows .NET 测试 102 项通过。测试阶段仅保留既有 NuGet 漏洞源不可达 `NU1900` 警告。
- Android APK SHA-256：`3E8FD0D3D7DAD0522294BEBAB12EFB18DFCA9EAE3282C05310206D3896C55D08`。
- Windows 安装器 SHA-256：`9D82D8EE5C149D7C45BEF8CACBF0A1606521A802F2E9757EA53B2C980B389A8B`。
- Windows 便携 ZIP SHA-256：`8355968676414C23C557E41F48D0E9C943435B4D140AA5D557AC512388448F2F`。

### 已知边界

- 普通第三方 Android 应用无法承诺在所有 vivo/其他 OEM 设备上锁屏静置数小时仍保持同一条 TCP；本版的目标是低功耗待命、需要时自动唤醒，BLE 失败时由常驻通知兜底。
- BLE Companion 关联、Windows 蓝牙适配器、Android 厂商后台策略、VPN 局域网访问权限、强行停止应用和真实 Wi‑Fi 漫游仍需要在目标设备上验收。

## [1.1.38] - 2026-09-21

### Fixed

- **优化文件传输吞吐和进度反馈**：发送端统一使用 2 MiB 文件分块，避免文件块逐帧刷新 socket，并为 Windows 文件读取启用顺序异步读取；接收端继续兼容旧版小分块。
- **修复首页传输记录进度跳变**：Windows 现在按约 100 ms 节流写入进行中的传输进度，并在记录区块内显示实时进度条，不再只显示 0% 和 100%。

## [1.1.37] - 2026-09-21

### Fixed

- **修复 Windows 任务栏图标显示为灰色占位图标**：任务栏和窗口现在优先直接加载多尺寸 `Assets/app_icon.ico`，首次激活后再次刷新任务栏绑定；桌面与开始菜单快捷方式也直接引用同一份 ICO，避免原生 AUMID 关联到无法解析的 EXE 图标资源。
- **保留 Windows 原生 AUMID 与稀疏包隔离边界**：不重新使用稀疏包身份，不改变 Explorer Shell 扩展的注册方式，避免回归此前的跨容器闪退问题。

## [1.1.36] - 2026-09-21

### Fixed

- **修复 Android 息屏静止约 180 秒后被误判离线**：Android 原生服务现在将后台无入站帧标记为 `Suspended`，保留 TCP 会话和原生探测；屏幕恢复后进行短时高频探测，只有真实 socket 故障或恢复探测超时才重连。
- **修复 Windows 过早销毁 Android TCP 会话**：Windows 对 Android 使用 20 秒心跳，连续静默进入 `Suspended` 而不主动 Dispose；收到任意合法协议帧后自动恢复 `Connected`。
- **统一跨端可用状态**：Flutter、Windows 工作区、剪贴板、通知、相册、日历、待办和投屏入口都把 `Suspended` 视为仍可恢复的已认证会话。
- **增加 Android 原生重连退避**：重连间隔采用 2s、5s、10s、30s、60s，避免 Doze 或网络切换时高频重试。

## [1.1.35] - 2026-09-21

> 本版将 GitHub 原 `v1.1.32` 的文件管理更新，与后续累积的 Android 长连接、VPN 兼容修复合并发布。相较于 GitHub 上一版 `v1.1.32`，本版重点提升后台连接在网络和 VPN 状态变化时的自动恢复能力。

### Included from the original v1.1.32 release

- **文件管理“手机存储”支持精准文件夹拖放传输与动效**：电脑文件可以直接拖拽至目标文件夹，宫格和列表视图均支持悬停高亮与目标提示，释放后投递到对应文件夹。
- **支持空白区域投递和外部资源管理器拖放**：手机存储子文件夹内拖到空白区域时使用当前文件夹；根目录和分类视图安全回退到 `/Download/Hinge`，外部资源管理器的 `WM_DROPFILES` 也使用同一套目标解析逻辑。
- **传输完成后自动刷新文件和相册视图**，无需手动点击刷新即可看到新接收内容。
- **细化 Android 接收端保存路径**：只有显式投递到 `/Download/Hinge` 时才按类型归档，投递到 `Download` 根目录或指定子文件夹时保留用户选择的目标位置。

### Fixed

- **增强 Android 后台长连接恢复**：原生前台服务负责 TCP 会话、心跳、断线重连和待发送任务队列；前后台使用不同的探测与超时策略，收到任意合法协议帧都会刷新连接存活时间。
- **支持 Android 网络状态变化后的自动恢复**：Wi-Fi/以太网网络变化、VPN 启停或 VPN 路由策略变化时，自动重建局域网监听、重绑定发现 socket，并按历史可信设备恢复连接；不会把 VPN 虚拟网卡当作局域网传输路径。
- **修复 VPN 场景 TCP 监听器仍沿用旧路由**：物理网络或 VPN 路由策略变化后，原生 TCP `ServerSocket` 会重新创建，确保反向连接不会继续使用 VPN 启用前的默认路由。
- **修复 Android 出站连接在 VPN 下超时**：原生 TCP 客户端优先使用物理 `Network` 的 `SocketFactory` 创建连接，并保留绑定失败时的诊断与兼容回退。
- **修复 VPN 切换后的 Flutter UDP 发现 socket 未及时重建**：原生网络策略变化会通知 Flutter，发现服务会串行重绑定，避免多个网络切换回调同时关闭和创建 socket。

### Verification

- Flutter 静态分析通过，Flutter 全量测试 63 项通过。
- Windows .NET 测试 98 项通过；Android Release APK、Windows 安装器和便携包均已构建并校验版本号。
- 第三方 VPN 的局域网访问权限、锁屏后台保持和真实设备重连仍需在目标手机上验收。

## [1.1.34] - 2026-09-20

> 相较于 GitHub 上一版 `v1.1.33`，本版双端源码版本为 Android `1.1.34+74`、Windows `1.1.34.0`。本版修复第三方 VPN 启停时 Android 局域网发现和连接恢复的回归。

### Fixed

- **修复 Android 启用独立 VPN 后暂时无法发现/重连 Windows**：前台服务现在独立监听 VPN 网络状态，在检测到 VPN 接管默认路由时，仅将 Hinge 进程临时绑定到当前物理 Wi‑Fi/以太网，保证 Flutter UDP 发现 socket 仍从局域网收发；VPN 关闭或物理 LAN 消失后立即解除该兼容绑定。
- **修复 VPN 启停后继续等待旧 TCP 超时**：VPN 路由策略发生变化时，主动关闭旧会话并按历史设备配置重新建链，同时增加物理网络短暂切换时的 250ms、1s、3s 兜底重探测。
- **保留普通网络的逐 Socket 绑定**：没有 VPN 时仍由原生连接服务把 TCP/发现信标绑定到当前物理 `Network`，不把整个 Android 进程固定在可能过期的 Wi‑Fi 网络上。

## [1.1.33] - 2026-09-20

> 相较于 GitHub 上一版 `v1.1.32`，本版双端源码版本为 Android `1.1.33+73`、Windows `1.1.33.0`。本版集中改造 Android 后台连接链路；Companion Device 仍作为后续可选增强，不纳入本版核心连接路径。

### Fixed

- **修复 Android 后台 TCP 连接过早判死**：前台保持 5 秒探测与 45 秒超时，息屏/后台改为 20 秒探测与 180 秒超时；收到任意合法协议帧都会刷新连接存活时间，不再只依赖心跳 PONG。
- **修复网络切换后沿用旧连接**：Android 前台服务不再使用进程级 `bindProcessToNetwork`，而是将原生 TCP 出站 socket 和服务发现 UDP socket 绑定到当前物理 LAN；Wi-Fi 漫游、DHCP 更新或网络句柄变化时主动关闭旧会话并按已配对设备配置重建连接。
- **增强 Android 后台 Wi-Fi 保活**：Android 10–13 同时申请低延迟与高性能 Wi-Fi Lock，避免低延迟锁在息屏后台失效；Android 14 及以上遵循系统对高性能模式的限制，仅使用低延迟锁。
- **保留主动断开语义**：网络重建仍尊重当前进程内的手动断开抑制，不会因为普通心跳或历史重连任务擅自恢复用户主动断开的连接。

## [1.1.32] - 2026-09-20

> 相较于 GitHub 上一版 `v1.1.31`，本版双端源码版本为 Android `1.1.32+72`、Windows `1.1.32.0`。以下只记录上一版之后的新增功能与修复，不重复 `v1.1.31` 已发布的内容。

### Added

- **文件管理“手机存储”支持精准文件夹拖放传输与动效**：在 Windows 文件管理“手机存储”页面，支持将电脑文件直接拖拽至目标文件夹上方（宫格视图与列表视图均支持）；拖放悬停时具备目标文件夹半透明高亮动效（`Opacity = 0.72`）与居中动效遮罩提示（“释放以发送到文件夹：xxx”），释放后精准投递至对应文件夹，不再强制定向到默认收件箱。
- **文件管理空白区域智能投递**：在“手机存储”子文件夹内拖拽文件至空白区域时，目标路径自动解析为当前打开的文件夹（动效提示“释放以发送到当前文件夹”）；根目录及非“手机存储”分类（最近文件、图片、视频、音频、文档、微信、QQ）时，安全回退到 `/Download/Hinge` 并由 Android 端按文件类型分类归档。
- **外部 Win32 消息拖放（WM_DROPFILES）对齐**：实现 `FindFolderAtRootPoint`，结合 WinUI VisualTreeHelper 与 DIP / 控件 Bounds 命中检测，使来自 Windows 资源管理器直接拖拽的文件享有完全一致的文件夹悬停高亮、动效提示与精准投递体验。
- **传输完成即时自动刷新**：文件传输成功后（`result.Completed > 0`），自动触发当前视图静默刷新（文件管理调用 `RefreshRemoteFilesAsync(forceRefresh: true)`，相册调用 `RefreshCurrentView()`），无需用户手动点击刷新即可即时看到新接收的文件。

### Fixed

- **Android 接收端保存路径精细化**：`_isHingeDropInbox` 仅针对显式 `/storage/emulated/0/Download/Hinge` 触发三分类，用户明确投向 `Download` 根目录或任意指定子文件夹时，原生保存至对应位置。

## [1.1.31] - 2026-09-20

> 相较于 GitHub 上一版 `v1.1.21`，本版双端源码版本为 Android `1.1.31+71`、Windows `1.1.31.0`。以下只记录上一版之后的新增功能与修复，不重复 `v1.1.21` 已发布的内容。

### Added

- Windows 和 Android 设置页新增可选的 6 位本机配对码，连接时使用随机挑战的 HMAC 校验；个性化设置入口和返回路径同步优化。
- Android 长期 TCP 会话下沉到原生前台服务，由服务持有监听、心跳、断线重连和持久化文件发送队列；新增应用私有滚动诊断日志，便于区分服务、网络、握手和心跳问题。

### Fixed

- 修复 Android 原生连接竞速、重复会话清理、历史端点保存和服务重建后的重连问题；明确区分自动清理与用户主动断开，避免错误抑制后续自动恢复。
- 修复 Android 原生桥在主线程写入 socket 触发 `NetworkOnMainThreadException` 的问题，连接建立后的业务帧和心跳回应改由单线程 I/O 队列顺序写入。
- 修复 Android 通知历史对抖音、小黑盒、酷安等应用的包可见性、图标读取和历史通知启动兜底，避免把已安装应用误报为已卸载。
- 修复 Windows 连接竞速任务、取消令牌释放和多个 `ContentDialog` 并发导致的驻留不稳定；补充启动生命周期日志。
- 修复 Windows 11 首次右键文件时 Explorer 冷激活右键扩展导致 Hinge 闪退、菜单项缺失的问题：主程序移除嵌入式 `<msix>` 身份，快捷方式统一使用 `Hinge.Office`，稀疏包移除多余的 `windows.fullTrustProcess` 扩展并关闭注册表/文件虚拟化，主进程与 Shell COM 容器彻底解耦。

## [1.1.30] - 2026-09-20

> 双端源码版本：Android `1.1.30+70`，Windows `1.1.30.0`。本版只记录相对 `1.1.29` 的 Windows 启动稳定性修复。

### Fixed

- 修复 Windows 启动时主动实例化 Explorer 右键菜单 COM 扩展，导致主窗口完成激活后仍以退出码 1 结束的问题；
- 应用和安装器不再主动预热右键菜单 COM 服务器，保留稀疏包静态注册、设备快照与 Shell 关联刷新，由 Explorer 在需要显示菜单时按正常机制加载扩展；
- 增加 Windows 启动阶段生命周期记录，便于后续区分窗口创建、网络监听和 Shell 集成故障。

## [1.1.29] - 2026-09-20

> 双端源码版本：Android `1.1.29+69`，Windows `1.1.29.0`。本版只记录相对 `1.1.28` 的原生连接写入修复。

### Fixed

- 根据真机连接诊断确认，修复 Flutter 通过 Android `MethodChannel` 发送协议帧时在主线程直接执行 socket 写入、触发 `NetworkOnMainThreadException` 的问题；
- Flutter 发出的协议帧现在经过原生单线程 I/O 队列按顺序写入，连接完成后的业务请求和心跳回应不再造成“连接成功后立即断开”；
- 原生服务停止时会同步停止桥接写入队列，服务关闭后的帧会被安全丢弃并留下诊断原因。

## [1.1.28] - 2026-09-20

> 双端源码版本：Android `1.1.28+68`，Windows `1.1.28.0`。本版只记录相对 `1.1.27` 的 Android 后台连接修复。

### Fixed

- Android 长期 TCP 会话重新交由原生前台服务持有，心跳、监听和历史连接恢复不再依赖 Flutter 页面或 UI isolate 持续运行；
- 修复原生连接请求刚进入工作队列就被 Flutter 当作“连接成功”的异步契约错误；现在只有身份认证完成后才返回成功，失效的历史 IP 不会再阻断其他可用地址的尝试；
- 修复匿名连接尝试被持久化、同一设备重复安排重连任务而形成连接风暴的问题；历史重连现在按设备合并，连接恢复或手动断开时会取消对应任务；
- 修复原生协议帧时间戳读取偏移错误，并保留 CPU/Wi-Fi 锁、物理网络监听和前台常驻通知；
- Android“保活设置”新增“复制连接诊断”，可导出服务生命周期、网络、心跳和重连记录，且不包含文件内容、验证码或配对码。

## [1.1.27] - 2026-09-20

> 双端源码版本：Android `1.1.27+67`，Windows `1.1.27.0`。本版只记录相对 `1.1.26` 的通知历史兼容修复。

### Fixed

- Android 使用 `MAIN + LAUNCHER` 意图范围声明通知来源应用的包可见性，修复抖音、小黑盒、酷安等实际已安装应用无法读取图标、被误报为“应用可能已被卸载”的问题，无需申请 `QUERY_ALL_PACKAGES`；
- 点击历史通知时增加按包名启动桌面入口和 Android TV 入口的兜底，即使部分系统的 `getLaunchIntentForPackage` 返回空，也会继续尝试打开应用；
- 应用图标读取失败不再永久缓存空结果，应用更新或系统包可见性恢复后可在后续刷新中重新获取。

## [1.1.26] - 2026-09-20

> 双端源码版本：Android `1.1.26+66`，Windows `1.1.26.0`。本版只记录相对 `1.1.25` 的连接回退修复。

### Fixed

- 根据真机测试中大量短连接和 `TIME_WAIT` 证据，暂时停用 Android 原生 TCP broker，恢复此前稳定的 Dart Socket 会话、握手、心跳和重连路径；
- Android 原生前台服务继续保留常驻通知、CPU/Wi-Fi/组播锁、物理网络监听和 UDP 发现，不再与 Dart 连接层争用 TCP 52831；
- 修复回退到 Dart 连接后，手动断开和 Flutter 状态销毁未正确关闭会话的问题；
- Windows 11 原生右键菜单扩展在安装完成和 Hinge 启动时主动预热，并在 Explorer 的 COM 代理进程内保持驻留，减少第一次右键缺少 Hinge、第二次才出现的问题；菜单枚举仍只读取本机快照，不等待主程序或网络。

## [1.1.25] - 2026-09-20

> 双端源码版本：Android `1.1.25+65`，Windows `1.1.25.0`。本版只记录相对 `1.1.24` 的连接回归修复。

### Fixed

- 修复 Android 原生前台服务在连接竞速时过早清理仍处于身份握手阶段的新会话，导致点击连接后立即断开的问题；重复连接只在会话真正完成认证后处理；
- 修复自动清理重复连接和失败连接尝试被误记为“手动断开”，避免原生服务错误抑制后续历史重连；
- 修复 Android 被动接入连接把对端 TCP 临时源端口保存为历史重连端口的问题；
- 修复 Flutter Activity/引擎重建时错误向原生前台服务发送手动断开，保留服务持有的健康 TCP 会话；
- 增强原生服务的监听重试、TCP KeepAlive 和 socket 关闭原因诊断，便于区分握手失败、协议错误、心跳超时和重复会话清理。

## [1.1.24] - 2026-09-20

> 双端源码版本：Android `1.1.24+64`，Windows `1.1.24.0`。

### Fixed

- 修复 Windows 连接竞速任务在连接完成或超时后仍访问已释放取消令牌，导致未观察异常、重连失败和驻留不稳定的问题；
- 修复连接失败提示、手动连接和其他页面对话框同时弹出时触发 WinUI `ContentDialog` 并发异常的问题；
- Windows 连接尝试现在会在结束前取消并等待所有后台连接路径，避免旧任务继续干扰下一次连接。

## [1.1.23] - 2026-09-20

> 双端源码版本：Android `1.1.23+63`，Windows `1.1.23.0`。本轮重点验证 Android 后台连接稳定性，当前先交付 Android 测试包。

### Added

- Android 长期 TCP 会话下沉到原生前台服务：服务独立持有 TCP 监听、握手、心跳、断线重连和连接状态，Flutter 只负责页面与业务状态桥接；
- Android 发送任务改为应用私有磁盘队列，服务重建或短暂断联后会保留任务，并在连接恢复后自动继续发送；
- 增加应用私有的滚动诊断日志，记录服务生命周期、网络、socket、心跳和重连结果，不记录验证码、文件内容或配对码。

### Fixed

- 修复 Android Flutter 页面退到后台或被系统回收后，常驻通知仍在但 TCP 连接不再执行心跳、需要重新进入应用才能恢复的问题；
- 修复服务未连接时文件分享只停留在页面内存队列、进程重启后无法继续发送的问题；
- 原生服务在 Flutter 界面未连接时仍可完成收到的文件传输，避免后台接收时积压大体积协议帧。

## [1.1.22] - 2026-09-20

> 双端安装包运行版本：Android `1.1.22+62`，Windows `1.1.22.0`。

### Added

- Windows 和 Android 设置页新增可选的 6 位本机配对码；设备发现只广播是否需要配对码，实际连接使用基于随机挑战的 HMAC 校验；
- 连接到已启用配对码的设备时，发起连接的一端会在连接前要求输入对方配对码；自动历史连接不会在后台弹出输入框；
- Windows 设置页的个性化入口改为带图标、说明和箭头的设置项，个性化页面增加明确的返回按钮。

### Fixed

- 修复连接会话在身份信息已到达但配对尚未通过时被错误视为可用的问题；
- 修复 Android 发现广播的配对状态未覆盖普通广播路径的问题。

## [1.1.21] - 2026-09-19

> 双端安装包运行版本：Android `1.1.21+61`，Windows `1.1.21.0`。

### Fixed

- 修复 Windows 首页“附近设备”区域中“重新搜索”和设备行“断开连接”按钮右边缘未对齐的问题，统一两者的右侧间距。

## [1.1.20] - 2026-09-19

> 双端安装包运行版本：Android `1.1.20+60`，Windows `1.1.20.0`。

### Fixed

- 优化 Windows 资源管理器右键“通过 Hinge 发送到”的加载速度：Shell 扩展现在缓存程序路径和设备快照，仅在注册表或快照文件确实发生变化时重新读取和枚举，减少每次右键时的冷加载等待；
- Windows 发布构建现在会使用稀疏身份包的本地签名证书对 Shell 扩展 DLL 签名，避免右键扩展以未签名 COM DLL 交付。

## [1.1.19] - 2026-09-19

> 双端安装包运行版本：Android `1.1.19+59`，Windows `1.1.19.0`。

### Fixed

- 修复 Windows 端由资源管理器右键发送或静默启动时，主窗口从未完成首次激活便被隐藏，导致 WinUI 进程在数秒后自行结束的问题；现在会先建立一次窗口生命周期，再立即隐藏到托盘；
- 修复未打包 WinUI 启动时 `LaunchActivatedEventArgs` 丢失资源管理器命令行的问题；现在会从真实进程命令行恢复 `--shell-send` 请求，Hinge 未运行时也能正确进入发送队列，而不是只打开主窗口；
- 当安装更新导致包内设置丢失、且用户没有明确关闭“最小化到托盘”时，Windows 端默认继续驻留后台，避免普通关窗被误判为退出；
- Windows 端新增轻量进程生命周期日志，可区分主实例注册、次实例重定向、托盘退出和异常的系统终止；
- 修复局域网连接超时后底层套接字任务继续在后台运行并产生未观察异常的问题，降低长期驻留时的异常噪音和不稳定风险。

## [1.1.18] - 2026-09-19

> 双端安装包运行版本：Android `1.1.18+58`，Windows `1.1.18.0`。

### Fixed

- 统一 Windows 端操作按钮的圆角外观；图标按钮现在使用与 WinUI 原生 ComboBox 一致的圆角矩形填充和边框层次，修复日历、文件管理、相册和通知历史等页面刷新按钮显得生硬方正的问题；
- 日历日期网格按钮保留无圆角的连续网格布局，不受全局操作按钮样式影响。

## [1.1.17] - 2026-09-19

> 双端安装包运行版本：Android `1.1.17+57`，Windows `1.1.17.0`。

### Changed

- 统一日历工具栏的 WinUI 3 原生图标资源；月份切换改用居中的 `Back` / `Forward`，与刷新、今天、创建和日期跳转按钮保持同一套 Fluent 图标视觉。

## [1.1.16] - 2026-09-19

> 双端安装包运行版本：Android `1.1.16+56`，Windows `1.1.16.0`。

### Fixed

- 修复资源管理器发送时遇到短暂断联或旧连接失效后，点击只唤起 Hinge、文件没有留下发送任务的问题；现在点击会先持久化任务，在线发送失败会保留未完成文件，下一次连接完成握手后自动继续发送；
- 修复 Windows 右键“通过 Hinge 发送到”持续累积历史设备的问题，现在只保留最近一次连接的可信机型；暂时断联时仍可选择该机型并加入待发送队列。


## [1.1.15] - 2026-09-19

> 双端安装包运行版本：Android `1.1.15+55`，Windows `1.1.15.0`。

### Fixed

- 修复 Android 应用更新后身份与信任数据路径不稳定，导致历史设备无法自动恢复连接的问题；应用更新重启后的自动连接不再受上一次进程内手动断开影响；
- 修复 Windows 资源管理器“通过 Hinge 发送到”把信任库中的陈旧设备记录全部展示出来的问题，当前发现/已连接设备按设备 ID 去重，长期断联的同名历史记录只保留最近一条，并继续保留断联排队发送入口；
- 修复 Android 启动阶段首次发现事件可能早于设备页订阅，导致历史可信设备未触发自动连接的问题；
- 修复自动连接启动检查新增延迟计时器未在页面销毁时取消的问题。

## [1.1.14] - 2026-09-19

> 双端安装包运行版本：Android `1.1.14+54`，Windows `1.1.14.0`。

### Fixed

- 日历读取改为以 Android 日历 Provider 的可见实例为唯一来源，移除 Hinge 根据短信/通知自行推导的日程，避免显示与手机日历当前选择不一致的内容；
- 日历不再通过 `Events` 表补读被手机日历隐藏的账号日程，并增加实例级可见状态判断和相同内容去重，修复本地日程重复显示；
- Windows 资源管理器右键发送在设备暂时断联时会创建本地待发送任务，恢复对应设备连接后自动发送；已成功发送的文件不会随任务重试重复发送，应用重启后队列仍可恢复。

## [1.1.13] - 2026-09-19

> 双端安装包运行版本：Android `1.1.13+53`，Windows `1.1.13.0`。

### Fixed

- 修复日历补读路径未遵循 Android 当前可见日历设置的问题：读取 `Events` 时现在与 `Instances` 一样按 `Calendars.VISIBLE` 过滤，被隐藏的本地日历不会再次混入 Google 日历结果；
- 修复部分日历 Provider 重复返回同一实例的问题，按日历、事件和开始时间去重，避免 Android 与 Windows 同时显示两条相同日程。

## [1.1.12] - 2026-09-19

> 双端安装包运行版本：Android `1.1.12+52`，Windows `1.1.12.0`。

### Fixed

- 修复 Android 息屏较长时间后常驻通知仍在、实际局域网会话却断开的恢复链路：前台服务会跟踪当前物理 Wi-Fi/以太网网络，后台仅在历史可信设备已断开时低频刷新网络发现并尝试重连；手动断开抑制规则仍然有效。

## [1.1.11] - 2026-09-19

### Added

- Windows 安装包多源智能路径记忆：自动探测正在运行的 Hinge 进程、`%ProgramData%\Hinge\install-location.txt`、HKLM/HKCU 注册表、`HKEY_USERS` 跨用户配置以及桌面/开始菜单快捷方式（`WScript.Shell`）指向的目标，覆盖更新时自动填入历史安装目录（如 `D:\hinge`）并切换为“更新”；
- Windows 安装目录机器级持久化：安装完成时无条件将安装路径保存到 `%ProgramData%\Hinge\install-location.txt` 与注册表 HKLM/HKCU 核心及卸载分支中；
- 独立 VPN / 复杂网络双向并行连接竞速（Parallel Connection Race）模型：针对 Android 全局 VPN 导致直接入站 TCP SYN 丢包的问题，电脑端发起连接时并发进行直接正向 TCP 尝试与多重 UDP 突发逆向通知（3 连发，间隔 120ms）+ 50ms 高敏反向会话监听，双向竞速连接，秒级穿透，解决独立 VPN 下从电脑端发起连接超时的问题；
- 网络适配器智能过滤：双端在枚举与排序物理网卡时调用 `IsVirtualOrVpnInterface`，严格排除 `tun`, `tap`, `ppp`, `vpn`, `wg`, `wintun` 等虚拟网卡干扰。

### Changed

- Windows 安装器性能大幅优化：内置 Payload 改用智能增量解压（Smart Incremental Extraction），比对大小与时间戳跳过相同文件；防火墙配置改用原生 `netsh.exe advfirewall` 命令，完全消除多轮 `powershell.exe` 启动开销，安装/更新耗时从 1~2 分钟缩减至 2~4 秒；
- Android 通知历史升级至数据库版本 3（新增 `(package_name, notification_key)` 复合索引）：
  - 智能识别进行中状态（包含 `FLAG_ONGOING_EVENT`、`FLAG_FOREGROUND_SERVICE`、`FLAG_NO_CLEAR`、`CATEGORY_PROGRESS`、`CATEGORY_SERVICE` 等）；
  - Google Play 应用下载（如每分钟更新一次进度）、音乐播放、常驻前台服务等周期性更新的通知，通过 `findLatestByNotificationKey` 匹配后在数据库**就地更新**单条记录，全程只保留 1 条记录，不再每分钟新增重复行；
  - 5 分钟内完全相同的非进行中通知自动去重合并，仅更新时间戳；
  - 移除通知时在 `onNotificationRemoved` 中自动归档为 `ongoing = 0`。

## [1.1.8] - 2026-09-17

> 双端安装包运行版本：Android `1.1.8+48`，Windows `1.1.8.0`。

### Changed

- 更换新图标，并减少 Windows 图标透明留白，使图标更适配深色桌面和任务栏显示。

## [1.1.7] - 2026-09-14

> 双端安装包运行版本：Android `1.1.7+47`，Windows `1.1.7.0`。

### Added

- Windows 日历改为整月网格视图：支持月份切换、跳转到今天、跳转到具体日期、创建新日程和刷新；选中日期后在下方查看完整日程详情；
- Android 日历读取补充日历实例、事件详情、日历来源、扩展属性和 OEM Provider 回退路径，能够保留日程的类型、地点、描述、颜色和来源信息；
- 增加对第三方/隐藏日历来源的补全，尽可能读取 OEM 日历 Provider 中可见的第三方日程；
- 增加日历创建入口，Windows 可直接唤起手机上的新建日程界面。

### Changed

- Windows 所有功能页统一收紧 NavigationView 内容顶部间距，去掉无内容 Header 造成的大块空白；日历顶部工具栏改为紧凑图标按钮，并区分“今天”和“日期跳转”；
- 日历支持按主题重绘，修复选中/悬停日期、日程文本、月份箭头在浅色和深色模式下对比度不足的问题；首页、文件管理、相册、通知历史、笔记和待办同步使用主题感知的正文与次要文字颜色；
- 日程类型显示更明确：生日标题保留完整信息，全天生日只归属于实际日期，不再错误延伸到次日；详情页不再直接显示 Provider 内部的 `reminder_alert_type` 等无意义字段；
- 主动断开设备后，本次进程生命周期内不会被历史自动重连立即恢复；重启应用后才重新启用历史连接自动恢复，手动连接仍可立即解除该抑制。

### Fixed

- 修复 Windows 手机历史通知点击微信时已在后台运行但无响应的问题；现在通过 Windows 的微信应用注册入口唤起现有实例，避免重新打开登录界面或显示不可操作的窗口；
- 修复 Windows 设备断开、网络瞬断或应用退出后，身份确认、心跳响应和剪贴板后台发送任务仍访问已释放连接的问题，避免未观察任务异常反复写入崩溃日志并导致常驻程序异常退出；
- 修复 Windows 11 资源管理器 Hinge 右键扩展的命名管道死锁：客户端不再调用会等待服务端消费的 `FlushFileBuffers`，发送请求后直接关闭句柄，由服务端通过 EOF 完成读取；
- 修复资源管理器兼容菜单在设备连接变化时短暂消失或残留旧设备的问题，更新子项时保持父菜单存在并校验菜单结构后再跳过重复写入；
- 新增日历日期范围测试，修复全天事件结束时间按 Android 的排他结束值处理错误导致生日显示到下一天的问题。

## [1.1.6] - 2026-09-14

> 双端安装包运行版本：Android `1.1.6+46`，Windows `1.1.6.0`。

### Fixed

- 彻底修复 EXE 安装版任务栏仍显示灰色占位图标的问题：稀疏包注册成功后，安装器将快捷方式切换到实际包 AUMID，并生成符合 Windows 资源限定规则的多档任务栏图标；
- Windows 窗口仍保留窗口级、窗口类和 `AppWindow` 三层图标设置，兼容未建立包身份的便携版；
- 增加安装器无人值守更新入口，用于自动化覆盖安装、重新注册稀疏包和验证发布产物。

### Packaging

- Android 与 Windows 产品版本统一为 `1.1.6`，Android build number 递增到 `46`；GitHub Release 继续作为稳定版直接标记为 `Latest`。

## [1.1.5] - 2026-09-13

> 双端安装包运行版本：Android `1.1.5+45`，Windows `1.1.5.0`。

### Fixed

- 修复 Windows 资源管理器原生右键菜单选择设备后只打开 Hinge、没有真正发起传输的问题；Shell 扩展现在优先通过当前用户专用命名管道把请求交给已经常驻的 Hinge 单实例，直接复用现有批量发送逻辑；
- Hinge 未运行时保留带参数启动的兜底路径，避免右键操作因进程尚未常驻而丢失。
- 修复 Windows 验证码托盘气泡点击后同时复制并恢复 Hinge 窗口的问题；验证码动作现在会抑制同一次交互产生的托盘打开事件，明确的托盘菜单“打开 Hinge”仍可正常打开窗口。
- 修复 EXE 安装版任务栏仍显示灰色占位图标的问题：安装器在稀疏包注册成功后，将开始菜单和桌面快捷方式改写为运行时真实 AUMID `Hinge.Office.Identity_29ecp0hep5z68!Hinge`，不再与未打包身份 `Hinge.Office` 混用；
- 稀疏包不再把同一张 256×256 图片冒充全部 Logo，现按 Windows 资源限定规则生成 44×44、150×150、50×50，以及 16/20/24/32/40/48/64/256 像素的 `targetsize` 和 `altform-unplated` 图标资源。

### Compatibility

- 资源管理器菜单仍由 EXE 安装版的稀疏身份包提供第一层入口；便携版继续使用兼容级联菜单。选择设备后，目标仍为 Android `Download/Hinge` 下按类型分类的目录。

## [1.1.3] - 2026-09-13

> 双端安装包运行版本：Android `1.1.3+43`，Windows `1.1.3.0`。

### Fixed

- 修复 Windows 任务栏按钮仍使用灰色通用图标的问题；启动时提前设置进程级 `Hinge.Office` AppUserModelId，主窗口创建后同时设置窗口图标、窗口类图标和 `AppWindow` 任务栏图标，开始菜单快捷方式统一使用发布目录中的真实 `.ico`；
- 修复 Android 通知历史验证码字段在转发给 Windows 时丢失的问题；Windows 历史通知现在会在确实识别为验证码且有具体数字码时显示“复制验证码”；
- 修复 Windows 兼容级联菜单父项默认值写入方式错误导致的文件关联弹窗；EXE 安装器注册原生 Explorer 扩展成功后会刷新当前用户的 Explorer，使第一层菜单无需手动重启 Explorer 才重新加载。

### Compatibility

- 安装器刷新 Explorer 时会重启当前用户会话中的 Explorer 进程；已注册成功的稀疏包和设备快照不会受影响。便携版仍使用“显示更多选项”中的兼容级联菜单。

## [1.1.2] - 2026-09-13

> 双端安装包运行版本：Android `1.1.2+42`，Windows `1.1.2.0`。

### Fixed

- 修复 Android 通知历史数据库版本号未同步的问题；升级已有安装时会按实际字段幂等迁移，避免通知监听或打开历史页面时闪退；
- 隔离异常的厂商通知数据，单条格式异常不会再结束通知监听服务；服务销毁时也不会无条件初始化未使用的历史数据库；
- 修复短信内容观察器在服务销毁与系统回调并发发生时向已关闭线程池提交任务的竞态，避免产生未捕获异常；
- 通知历史验证码识别改为对所有应用通知使用严格的“验证码关键词 + 独立数字码”规则，迅雷等应用通知现在也会显示“复制验证码”，普通数字通知不会误显示；
- 修复 Windows 窗口预览图标没有加载真实 Hinge 图标的问题，发布负载现在同时带有 `.ico` 资源并显式设置窗口大小图标；
- 修复 Windows 资源管理器原生右键扩展在设备快照暂时读不到时直接隐藏的问题；根菜单保持可见，无连接时显示禁用提示，并增加本用户本地快照文件作为 COM 宿主读取注册表失败时的兜底；
- 修复兼容级联菜单父键未明确声明为空默认值导致发送动作落入文件关联错误的问题。

### Compatibility

- Windows 11 原生第一层菜单仍要求 EXE 安装版的稀疏包身份；安装或更新后如果 Explorer 尚未重载扩展，请重启 Explorer。便携版继续使用兼容级联菜单。

## [1.1.1] - 2026-09-13

> 双端安装包运行版本：Android `1.1.1+41`，Windows `1.1.1.0`。

### Fixed

- 修复 Windows 发布目录遗漏 `app_icon.png` 导致任务栏图标和窗口预览图标退化为通用图标的问题；
- 增加未观察任务和 WinUI UI 异常的记录/隔离，避免单个后台回调异常直接结束常驻进程，同时保留 `crash.log` 诊断记录；
- 修复资源管理器“显示更多选项”兼容菜单的级联注册结构：`ExtendedSubCommandsKey` 及其 `Shell/command` 现在位于正确的注册表层级，避免点击后错误地尝试打开原文件；
- 通知历史默认按最新到最早排序；Android 与 Windows 的短信验证码通知历史条目增加“复制验证码”操作，普通通知不显示复制操作；

### Compatibility

- 保留 Windows 11 稀疏包身份提供的第一层原生 `IExplorerCommand`，并持续写入当前用户级兼容菜单；安装或更新后如 Explorer 尚未重载扩展，重启 Explorer 后再验收第一层菜单。

## [1.1.0] - 2026-09-13

> 双端安装包运行版本：Android `1.1.0+40`，Windows `1.1.0.0`。

### Added

- 新增 Windows 11 资源管理器第一层“通过 Hinge 发送到”菜单；鼠标悬停后动态列出已完成连接的机型，支持对选中的一个或多个文件选择目标设备；
- 资源管理器发送复用现有文件传输链路，Android 端统一写入 `Download/Hinge` 并按视频、图片和文件自动分类；
- 增加原生 `IExplorerCommand` 扩展、资源管理器多选路径解析、设备连接等待、托盘结果通知和卸载清理；
- EXE 安装器内置并注册稀疏包身份，仅安装时为公开签名证书请求一次本机信任；GitHub 仍只发布 EXE 与便携 ZIP，不新增独立 MSIX 资产。

### Compatibility

- EXE 安装版使用 Windows 官方包身份与原生 Shell 扩展进入 Windows 11 第一层菜单；便携版无法自动建立可信包身份，因此保留“显示更多选项”中的当前用户级兼容菜单。

### Compliance

- 原生 COM 激活框架改编自 Microsoft `vscode-explorer-command`（MIT）；动态设备枚举、注册表快照与 Hinge 单实例传输逻辑由本项目实现，许可证登记已同步。

## [1.0.38] - 2026-09-13

> 双端安装包运行版本：Android `1.0.38+39`，Windows `1.0.38.0`。

### Fixed

- 修复 QQ/微信隐藏到托盘时误显示内部 Chromium 窗口，导致出现没有任务栏入口且无法交互的假窗口；
- 隐藏状态改为查询客户端自己的 Electron 托盘宿主和真实通知图标 ID，再投递与用户单击托盘图标等价的回调，由客户端自行恢复真实界面；
- 过滤无标题渲染窗口和托盘宿主，只对客户端已经自行显示的带标题顶层窗口执行前台激活；

### Compliance

- 补充 Electron MIT 公开实现的兼容参考记录；Hinge 没有复制或打包 Electron 源码，也没有新增 Electron 运行时依赖。

## [1.0.37] - 2026-09-13

> 双端安装包运行版本：Android `1.0.37+38`，Windows `1.0.37.0`。

### Fixed

- 修复更新安装时安装器结束 Hinge 整个进程树，导致由 Hinge 唤起的 QQ/微信也被一并退出的问题；安装器现在只结束 Hinge 本身；
- 移除 QQ/微信窗口唤醒中的跨进程输入队列绑定、同步窗口恢复和强制置顶，避免 Chromium/Electron 客户端卡死或自我重启；
- 改为异步请求显示真实主窗口并执行标准前台激活，客户端 UI 线程仍由客户端自身管理；
- 保持已有 QQ/微信进程时绝不启动第二个实例，并在真正启动前再次检查运行状态，避免新的登录窗口。

## [1.0.36] - 2026-09-13

> 双端安装包运行版本：Android `1.0.36+37`，Windows `1.0.36.0`。

### Fixed

- 修复点击 QQ 历史通知后显示黑色空窗口的问题；
- Windows 只接受带标题栏、非 owner、具有用户可见标题的 QQ/微信顶层窗口，明确排除 `Chrome_WidgetWin_0` 渲染壳、托盘宿主和输入法窗口；
- 增加全局顶层窗口探测和 `QQEX` 运行态识别，降低更新后已有 QQ 被误判为未运行、进而重复启动登录实例的概率；
- 保持已有 QQ/微信进程优先唤醒，匹配进程存在但窗口暂不可用时不启动第二个客户端。

## [1.0.35] - 2026-09-13

> 双端安装包运行版本：Android `1.0.35+36`，Windows `1.0.35.0`。

### Fixed

- 修复 Windows 激活 QQ/微信隐藏主窗口时调用错误 DLL 导致通知打开失败的问题；
- 确保 `GetCurrentThreadId` 从 `kernel32.dll` 调用，输入队列绑定可以正常执行；
- QQ/微信已有进程时继续禁止启动第二个实例，避免出现新的登录界面。

## [1.0.34] - 2026-09-13

> 双端安装包运行版本：Android `1.0.34+35`，Windows `1.0.34.0`。

### Fixed

- 修复 QQ 已在后台运行时仍启动新的登录界面的问题；
- 识别 QQ 隐藏到托盘后无标题但保留完整尺寸的 Chromium 主窗口，并直接恢复已有窗口；
- 只要检测到 QQ/微信进程已存在，就不再启动第二个客户端实例。

## [1.0.33] - 2026-09-13

> 双端安装包运行版本：Android `1.0.33+34`，Windows `1.0.33.0`。

### Fixed

- 修复 Windows 点击手机历史通知时，QQ/微信已经在后台运行但窗口句柄为空或前台切换失败，导致点击没有任何响应的问题；
- 仅针对匹配客户端进程查找真正的顶层主窗口，并临时处理跨进程输入队列，避免激活 QQ/微信子窗口或渲染窗口；
- 支持识别 QQ/微信隐藏到托盘时无标题但保留完整尺寸的 Chromium 主窗口并直接唤醒；只要客户端进程已存在就不启动第二个实例，避免弹出新的登录界面。

## [1.0.32-dev.1] - 2026-09-13

> 双端安装包运行版本：Android `1.0.32+33`，Windows `1.0.32.0`。

### Fixed

- Windows 点击 QQ/微信通知时只唤醒已运行客户端的主窗口，移除顶层窗口枚举和子窗口操作，避免 QQ 多进程渲染窗口被错误激活后卡死；
- QQ/微信通知不再调用深链或 Android 原通知入口，避免唤起额外登录、聊天或渲染界面；
- 客户端未运行时仍可从已发现的安装路径启动，运行中的客户端不会再启动第二个进程。

## [1.0.31-dev.1] - 2026-09-13

> 双端安装包运行版本：Android `1.0.31+32`，Windows `1.0.31.0`。

### Fixed

- Windows 点击 QQ/微信历史通知时，先查找并恢复已经运行的桌面客户端及其顶层窗口，不再重复启动第二个进程而进入新的登录界面；
- 自动连接失败后增加延迟重试，并将自动重连请求明确标记为历史设备恢复请求，避免设备广播内容不变时只尝试一次；
- 历史设备自动重连继续以双方持久化的设备身份和信任记录为准，新设备和未信任请求仍不会跳过手动连接。

## [1.0.30-dev.1] - 2026-09-12

> 双端安装包运行版本：Android `1.0.30+31`，Windows `1.0.30.0`。本开发版修复 Windows 通知历史中 QQ/微信桌面客户端打开失败的问题。

### Fixed

- Windows 点击 QQ/微信历史通知时，不再盲目调用未注册的 `mqq://` 或 `weixin://` 协议，避免弹出“获取打开此链接的应用”系统对话框；
- 增加对 QQ/微信常见安装目录、App Paths、卸载注册表信息和当前运行进程的探测，覆盖自定义盘符安装（例如 `D:\QQ`、`D:\Weixin`）；
- 已安装桌面客户端时优先启动本机客户端；只有确认协议已注册时才使用 URI 深链，未找到桌面客户端时继续安全回退到 Android 原通知入口；
- 微信和 QQ 共用同一套安全启动链路，避免一端修复而另一端继续弹出协议错误。

## [1.0.29-dev.1] - 2026-09-12

> 双端安装包运行版本：Android `1.0.29+30`，Windows `1.0.29.0`。本开发版修复多网络环境下的设备发现与连接链路。

- Android 启动 TCP/UDP 服务前优先绑定当前可用的 Wi‑Fi 网络，避免蜂窝网络与 Wi‑Fi 并存时局域网 socket 走错网络；回到前台时只重绑发现 socket，不影响现有会话。
- UDP 固定发现端口被占用时，改用真实的临时监听端口，并在发现报文中携带 `discoveryPort`；旧客户端缺少该字段时继续使用 52830。
- Windows 与 Android 的反向连接请求、设备注册和持久化模型同步使用发现端口，避免发现成功后反向连接仍投向旧端口。
- 加强 Android 网络服务启动容错，避免短暂网络切换阻断发现服务初始化。

## [1.0.28-dev.1] - 2026-09-12

> 双端安装包运行版本：Android `1.0.28+29`，Windows `1.0.28.0`。本开发版新增通知历史，采集默认关闭。

### Added

- Android 新增“工作区 > 通知历史”：用户授予系统通知访问权限并主动开启采集后，记录应用图标、名称、标题、正文、时间和通知状态；支持最早到最晚、最晚到最早以及按应用筛选。
- Windows 侧边栏新增“手机历史通知”：从已连接 Android 设备分页读取历史，首屏最多 100 条，滚动时继续加载，并支持相同的时间顺序和应用筛选。
- 微信/QQ 历史通知点击优先复用原通知入口，通知仍有效时可回到原聊天定位；入口失效时安全回退为打开应用。
- 更新协议、架构、开发状态、兼容性、路线图和 README，明确通知访问授权、私有数据库和真实设备验收边界。

### Changed

- Android/Windows 产品版本统一推进到 `1.0.28`，Android build number 推进到 `29`。
- 通知历史与短信转发使用同一个 Android 通知监听服务，但开关、存储和展示链路彼此独立；通知历史默认关闭，不读取短信收件箱历史。
- Android 本地发布产物统一为 `publish/Hinge.apk`；发布脚本优先使用固定 BKS 路径并在签名、ARM64 架构和证书校验通过后才复制产物，避免调试签名或错误签名包覆盖更新包。

### Verification

- Android Release Kotlin 编译和 ARM64 APK 元数据核对通过；
- Windows WinUI 解决方案 Release 构建通过；
- Android Flutter tests：54 项通过；Windows .NET tests：63 项通过；
- 新增能力仍需在真实 Android 通知访问授权、微信/QQ 通知入口和不同厂商后台策略上验收。

## [1.0.27-dev.1] - 2026-09-09

> 双端安装包运行版本：Android `1.0.27+28`，Windows `1.0.27.0`。本开发版调整短信验证码通知交互，并继续保持普通托盘通知行为。

### Added

- Android 增加可选的短信/彩信同步链路：明确授权后监听新到消息，并通过已建立的可信会话转发到 Windows；不扫描历史短信。
- Windows 短信/彩信固定使用托盘气泡，避免用户必须打开 Hinge 窗口才能看到验证码。
- 识别为验证码的短信/彩信支持点击整条托盘气泡复制验证码；普通短信和其他通知不会触发复制。

### Fixed

- 修复短信被原生 Toast 或应用内浮层接管、导致后台接收时不符合预期的问题。
- 修复托盘通知点击行为：只有验证码消息绑定复制动作，其他通知仍按普通通知打开 Hinge。
- 保留 Android `RECEIVE_SMS`、`READ_SMS` 和厂商兼容回退路径的授权引导与后台保活逻辑。

### Packaging

- Android 发布包继续只包含 `arm64-v8a`，包名为 `com.hinge.office`。
- Android 和 Windows 产品版本统一为 `1.0.27`；Windows 继续只发布自包含 EXE 安装器和便携 ZIP。
- 本版 Android APK 使用既有稳定签名重新签名，支持覆盖更新。

### Verification

- Android Flutter tests：53 项通过。
- Windows .NET tests：63 项通过。
- APK 包名、版本号、`arm64-v8a` 架构及 V2/V3 签名校验通过。
- Windows EXE 文件版本校验为 `1.0.27.0`。

### Known limitations

- 传统 Windows 托盘气泡不支持嵌入式按钮；验证码使用“点击整条气泡复制”的交互。
- Android 厂商的短信权限、通知权限和后台策略仍可能影响实时转发与长连接。

## [1.0.17-dev.2] - 2026-09-08

> 双端安装包运行版本：Android `1.0.17+18`，Windows `1.0.17.0`。本开发版重点修复大型手机文件库读取慢和首屏阻塞问题。

### Performance

- Android 文件管理、相册、微信和 QQ 分类改为 MediaStore 分页读取，首屏只查询当前批次，不再先构造完整文件列表。
- 文件总数通过轻量计数查询并短时缓存，读取详情时减少逐条文件系统目录探测，避免把目录误识别成 0B 文件。
- Android 工作区命令在单条连接内使用有上限的并发调度，缩略图和文件元数据可以并行读取，同时避免无上限线程占满设备。
- 保留旧客户端协议兼容，并继续使用双方协商的 ZLIB 压缩传输大型控制 JSON。

### Packaging

- Windows 发布资产固定为自包含 `Hinge-Setup.exe` 和便携版 `Hinge-Windows.zip`，不再提供 MSIX 或测试证书；EXE 安装器启用 .NET 单文件压缩以减小下载体积。

### Added

- Android/Windows 会在会话握手中协商大型控制 JSON 的 ZLIB 压缩；只压缩确实能缩小的工作区、同步、剪贴板、通知和文本数据，旧客户端保持兼容。
- Android 文件管理增加 OEM MIME 缺失/错误时的扩展名回退和常见文件头识别，并跳过被 MediaStore 错误返回的目录项。
- 新增按需媒体参数通路：图片读取尺寸与可用 EXIF 相机信息，音视频读取时长、分辨率、旋转、码率和基础标签。
- Windows 打开媒体时继续使用用户的默认应用，同时在不阻塞启动的前提下显示可获取的媒体参数。

### Compliance

- 记录 vivo 开源声明的候选组件审查结果；本次没有新增第三方运行时依赖，README、依赖审计和许可证登记已同步说明。
- README 增加直接运行时依赖、开发测试依赖、许可证、品牌 SVG 来源、vivo 开源声明审查边界和签名材料处理说明。

## [1.0.17-dev.1] - 2026-09-08

> 双端安装包运行版本：Android `1.0.17+18`，Windows `1.0.17.0`。这是面向真实设备验收的开发版，不代表所有厂商和网络环境都已完成验证。

### Added

- 增加当前工程的开发文档入口，统一记录架构边界、构建命令、测试门槛、发布资产和真实设备验收边界。
- Windows 文件管理和相册继续使用 200 项首批加载、滚动追加、缩略图缓存和请求取消，减少大媒体库打开时的 UI 阻塞。

### Changed

- Android 与 Windows 的产品版本号统一推进到 `1.0.17`，Android build number 推进到 `18`。
- 发布说明、开发状态、路线图和构建流程同步到当前 Hinge 工程，而不是继续引用首版 `1.0.0` 的过期基线。
- 保留 `publish/` 和 `tmp/` 为本地构建目录；安装包作为 GitHub 开发版资产发布，不把二进制和签名材料写入源码提交。

### Verification

- Android Flutter tests：49 项通过。
- Windows .NET tests：59 项通过。
- Android Release APK 使用既有稳定签名库重新签名并验证；Windows EXE、便携 ZIP 和可用的 MSIX 重新生成。

## [1.0.16] - 2026-09-08

### Fixed

- Made the Windows custom receive directory a real persisted setting, stored in the user profile/registry so EXE updates do not reset it.
- Applied the selected receive directory to multi-file and multi-photo saves instead of silently falling back to `Downloads\Hinge`.
- Replaced the storage-path text dialog with a native Windows folder picker and showed the selected directory in Settings.
- Kept `Hinge` as the only Windows title-bar label; page names no longer replace it or appear beside the old product subtitle.
- Aligned the photo-page selection, view, sort, and refresh controls to the same 36 px toolbar height.

## [1.0.15] - 2026-09-08

### Fixed

- Serialized incoming file frames and isolated each partial file by transfer ID, preventing multi-file saves from racing on the same `.part` file and crashing the Windows client.
- Converted receive-side file errors into a failed transfer result instead of an unhandled network callback exception.

## [1.0.14] - 2026-09-08

### Fixed

- Aligned the Android keep-alive checklist icons and right-side status/actions in a consistent centered layout.

## [1.0.13] - 2026-09-08

### Fixed

- Guarded the existing Windows file-management batch-save button against repeated clicks while a batch is in progress.

## [1.0.12] - 2026-09-08

### Fixed

- Added selection mode to the Windows photo grid, including multi-select and batch “保存到电脑”.
- Serialized remote media receives and guarded repeated save actions so multi-photo saves cannot race the pending transfer state and hang or crash the client.

## [1.0.11] - 2026-09-08

### Fixed

- Reconciled duplicate nearby-device records by platform, display identity, and shared LAN address; connected records are preserved while stale rows are removed.
- Ignored stale Windows self-discovery broadcasts from local network adapters, preventing an older Hinge process or installation from appearing as a nearby device.

## [1.0.10] - 2026-09-08

### Changed

- Removed the workspace's floating plus buttons and the note/task card plus buttons.
- Replaced the note/task page discovery refresh action with a pencil action; the desktop combined page offers both new-note and new-task choices from the pencil menu.
- Removed the discovery refresh actions from the calendar and album headers.
- Android home now resolves the local phone brand from the device identity, so the bundled local brand SVG is shown instead of the generic phone icon.

## [1.0.9] - 2026-09-08

### Changed

- Moved the floating capsule's bottom clearance into each scrollable page so the final content can be scrolled fully above the capsule without being covered.
- Replaced the mobile workspace action labels with a static rounded-square plus button at the original action position.
- Reordered Android settings to Personalization, Storage, Keep-alive, Default apps, and About; moved vibration feedback into Personalization.
- Simplified the settings entry for About to show only the app name and version; detailed information remains inside the About dialog.

## [1.0.8] - 2026-09-07

### Fixed

- Floating capsule navigation now reserves the device's bottom safe area plus an extra scroll buffer, so the end of the home page can be brought fully above the capsule.
- Replaced the mobile workspace's animated extended action button with a static action button positioned above the capsule.
- Cached Android default-app icon providers so changing one file type does not briefly redraw all three type rows.
- Reused the Windows client's locally bundled official brand SVGs in Android and show the connected phone brand logo in the Hinge Work header.

## [1.0.7] - 2026-09-07

### Fixed

- Android floating capsule navigation now reserves bottom layout space so it no longer covers home, notes, or tasks content.
- The mobile workspace action follows the selected section and displays “新建待办” for tasks; the task date picker uses Chinese localization.
- Android album loading now uses 200-item pages with incremental loading and bounded thumbnail concurrency to keep large local albums responsive.
- Default-app discovery runs off the Android UI thread, and the selected app icon is shown beside each media/file type.

所有面向用户的版本变化记录在这里。版本日期使用 Asia/Shanghai 时区。

## [1.0.6] - 2026-09-07

- 安卓默认应用选择器显示真实应用图标，并通过多种标准 MIME/Intent 组合发现更多兼容应用；同一个应用可同时出现在图片、视频和文件类型中。
- Windows 文件管理在滚动条进入后段前预取下一页，追加内容后主动复查滚动位置，并继续为新批次加载媒体缩略图。

## [1.0.5] - 2026-09-07

- 修复最近文件把手机文件夹显示成 0B 文件的问题。
- 增强 MediaStore 查询兼容性，避免部分厂商设备读取失败。
- 合并同一台手机因重新安装或设备 ID 变化产生的离线重复记录。

## [1.0.4] - 2026-09-07

- 修复安卓“默认应用”列表为空的问题，按内容 URI 和 MIME 类型列出可用应用，支持保存图片、视频、文件三类打开选择。
- 安卓照片、视频和音频页面改为要求对应的媒体权限；“所有文件访问”不再伪装成已授予媒体权限。
- 加强最近文件过滤：排除 Android 应用数据目录、日志/数据库目录、隐藏文件和更多缓存命名模式。

## [1.0.3] - 2026-09-07

- 改进最近文件过滤：识别真实媒体路径、URL 编码缩略图和明确的零字节占位文件，不按文件大小误删正常用户文件。
- 安卓通知点击改为按图片、视频和文件类型打开接收文件；新增“默认应用”设置，可分别选择处理应用。
- 增加 Android FileProvider，避免通知打开私有接收文件时复制到公共目录。
- 修复安装器“浏览”按钮在系统缩放下的文本裁切，保持 EXE 安装方式不变。

## [1.0.1] - 2026-09-07

- 修复项目重命名后 Windows 防火墙规则仍指向旧可执行文件，导致 UDP 可发现但 TCP 会话超时的问题。
- 新增双向竞速连接与 UDP 反向连接请求；任一方向的 TCP 入站受阻时，由另一端主动建立会话。
- EXE 安装程序统一申请管理员权限，并校验防火墙配置是否成功。
- Android 悬浮导航改为不占满底部内容区的紧凑胶囊。

## [1.0.0] - 2026-09-07

Hinge 首个公开版本，统一 Android 与 Windows 客户端的产品名称、资源引用、安装包名称和协议标识。

### Included

- Android ARM64 APK，使用稳定签名库构建；
- Windows WinUI 3 自包含 EXE 安装器与便携 ZIP；
- 局域网设备发现、会话、文件传输和剪贴板同步；
- Windows 文件管理、相册、笔记、待办、日历、工具和设置页面；
- Android 工作区、Monet 动态取色、预置主题和后台保活设置；
- 文件与相册分页加载、缩略图预算、类型筛选、排序和目录导航；
- Windows 系统默认文件关联打开图片、视频和音频；
- Windows 外部文件拖入、目标预览和应用内文件拖出取消区域；
- 项目文档、协议文档、构建脚本和交付说明统一使用 Hinge 品牌。

### Known limitations

- 手机投屏、真实通知回复和 OCR 没有作为本版本的完成能力交付；
- Android 厂商省电、锁屏和后台策略仍可能影响长连接；
- 大型微信/QQ 媒体库、弱网、多网卡和不同 Windows 防火墙配置仍需继续进行真实设备验收。

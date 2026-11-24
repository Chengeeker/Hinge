Warning: truncated output (original token count: 26954)
Total output lines: 1308

# Changelog

## [3.0.1] - 2026-11-10

- Windows 修复关机期间无线 ADB 后台重连仍可能启动 `adb.exe`，导致系统弹出 `0xc0000142` 初始化失败的问题。
- 收到 Windows 会话结束请求后暂停 ADB 和投屏启动。如果取消关机，自动重连恢复。

- Windows 文件管理的手机与 WebDAV 来源标签改用 Material 3 选中态，适配浅色和深色主题。
- 统一手机与 WebDAV 文件操作确认框的按钮样式，删除操作默认聚焦取消。

- Windows 新增无线 ADB 扫码配对。电脑生成二维码，手机通过系统无线调试扫码。配对成功后自动尝试连接，六位配对码仍可用。

## Android 3.0.1

- 新增「设置 → 手机服务 → 无线 ADB」独立入口，提供扫码配对和配对码配对的系统跳转与操作提示。
- 新增独立「剪贴板同步」设置入口。开关可以提前开启并保存偏好，连接无线 ADB 且电脑端允许同步后生效。
- 开启剪贴板同步后显示简短连接说明，并可直接前往无线 ADB 设置；关闭后暂停新文字的双向同步，保留已有配对，不影响投屏与文件传输。
- 设置页沿用 Material 3 排版、浅深色和大字号适配，点击反馈遵循应用触感开关，避免同一次操作重复震动。
- Android 版本名更新为 **3.0.1**，内部构建号 **195**，沿用原包名与签名，仅提供 **arm64-v8a** 安装包。

### 使用说明

- 系统是否允许直达无线调试或扫码页取决于手机系统。部分机型仍需在开发者选项中手动进入「无线调试」，本版不保证所有机型一键直达。
- 更新后建议在电脑端重连一次无线 ADB，使剪贴板桥加载新版设置开关。
- Android 相关的 21 项自动化测试通过，Release 构建、签名、ARM64、ZIP 完整性和 16 KiB 对齐校验通过；真实扫码、双向剪贴板和物理振感仍需设备验证。

完整说明见 [v3.0.1 中文更新日志](docs/CHANGELOG_v3.0.1.md)。

## [3.0.0] - 2026-11-07

对照上一正式发行版 [v2.2.0](https://github.com/Chengeeker/Hinge/releases/tag/v2.2.0)。

- 新增手机查看并控制 Windows 桌面，提供全屏、鼠标与触控手势、键盘、仅观看和系统快捷操作。
- 新增手机触控板、全屏游戏模式、按键映射、多套预设及预设导入导出，支持电脑编辑布局并同步至手机。
- Android 迁移至 Kotlin 与 Jetpack Compose，更新 Material 3 页面和液态玻璃悬浮底栏。
- 双端统一投屏与远控设置入口，Windows 新增 ADB 指令区和托盘传输文件入口。
- 新增配对码更新后撤销旧会话与重新验证，修复设备列表手动断开后自动连回、连接条目重复等问题。
- 更新 Windows Material 3 色彩、圆角和公共控件，相册集与照片列表支持自适应列数，改进相册原图打开及右键集成复用。

完整功能、升级条件、验证范围及发行文件校验值见 [v3.0.0 中文更新日志](docs/CHANGELOG_v3.0.0.md)。

Android 为 3.0.0+188，仅 arm64-v8a；Windows 为 3.0.0.0。

## [2.2.0] - 2026-10-23

对照 GitHub 上一正式发行版 [`v2.0.2`](https://github.com/Chengeeker/Hinge/releases/tag/v2.0.2)，本版新增 Windows 发起的 Android 手机投屏与远程控制、Android 传输进度通知和液态玻璃底栏选项，并更新桌面界面。完整发行说明见 [v2.2.0 更新日志](docs/CHANGELOG_v2.2.0.md)。

## [2.0.2] - 2026-10-15

- 修复 Android 与 Windows 并行连接时可能误关闭最终保留会话的问题；自动重连只清理真正多余的连接，并在重新登记会话前确认仍已就绪。
- 修复 Android 失效会话仍被当作“已连接”的重试问题；安静执行自动恢复，避免反复弹出连接提示。
- 修复多个 Android Activity/Flutter engine 交替时旧实例清理新实例事件接收器的问题；文件/相册查询及短信桥在前台恢复时重新挂接。
- Android：`2.0.3+124`，包名 `com.hinge.office`，仅 `arm64-v8a`；Windows：`2.0.2.0`。
- Android 定向 Flutter 回归 24/24、Windows 会话/配对回归 8/8；连接竞争代码自检 4 个情景通过。用户现场确认文件列表刷新与“发送测试消息到 Windows”均已恢复（当时设备仍运行 Android 2.0.2+123 / Windows 2.0.0.0）。更新版安装后的长时间及锁屏后台行为仍待验收。

详见 [v2.0.2 更新日志](docs/CHANGELOG_v2.0.2.md)。

## [2.0.1] - 2026-10-14

- 修复 Android 短信/彩信在跨端会话仍处于握手阶段时被静默丢弃的问题：等待可用会话后再发送，最多暂存 8 条于内存中，不写入文件或诊断日志。
- Windows 无线 ADB 的“连接 / 重连”会对已验证设备执行定向重连，并再次核验设备身份；手动连接后保留发现的实际 IP/端口。后台恢复继续复用健康连接，避免无必要的剪贴板中断。
- Android：`2.0.1+122`，包名 `com.hinge.office`，仅 `arm64-v8a`；Windows：`2.0.1.0`。
- Flutter 静态分析通过，Android ARM64 签名包与 Windows Release 包已构建并核验。构建机无法访问 NuGet 在线漏洞数据源，因此未完成在线依赖漏洞检查；Android→Windows 短信实机收发及 Windows 设置页按钮的安装后 GUI 验收尚未完成。

详见 [v2.0.1 更新日志](docs/CHANGELOG_v2.0.1.md)。

## [2.0.0] - 2026-10-11

本次以 Windows 旧版界面 v1.4.14 为对照基线，纳入之后完成的 Material 3 页面升级、窗口与导航调整、日历交互、字体系列/字重管理、品牌 Logo、离线发送保护、无线 ADB 剪贴板桥接、连接恢复和安装器路径修正；并修复 Android 设备页未及时显示 Windows 主动连接状态的问题。跨端传输协议保持 `0.1`；Android `versionCode` 递增以支持覆盖更新。完整列表见 [v2.0.0 更新日志](docs/CHANGELOG_v2.0.0.md)。

### 发布后修复（2026-10-12）

- Windows 无线 ADB 使用独立 Server 端口 `5038`，减少与其它 ADB 工具共享默认 `5037` Server 引发的版本和 mDNS 冲突；沿用原主机密钥与配对授权。
- Windows 测试 `199/199` 通过，独立 Server 的 mDNS 检查通过；真实手机连接和电脑重启恢复尚未验收。GitHub v2.0.0 的 Windows 安装器与便携 ZIP 已更新，APK 保持不变。

- Android：`2.0.0+121`，包名仍为 `com.hinge.office`，仅 ARM64。
- Windows：`2.0.0.0`。

### 验证状态

Android Flutter 测试 `77/77` 通过（设备页定向测试 `12/12`）。Android 与 Windows Release 资产均构建成功并完成包元数据/完整性核对；未进行 Android 真机验收。Windows 安装器外层 Authenticode 未签名。

## [1.4.14] - 2026-10-09

对照 GitHub 上一正式发行版 [`v1.4.9`](https://github.com/Chengeeker/Hinge/releases/tag/v1.4.9)，只记录之后进入本次发行的变化。

### Android 界面

- 按轻量 M3 规范统一首页、工作区和设置页的间距、字号及视觉层级；精简重复的静态功能说明，并移除个性化页明暗模式选项之间多余的分隔线。
- 普通页面采用全宽滑入并覆盖旧页面，底栏页面切换使用新旧页面反向平移；页面绘制不透明主题底色，避免切换期间旧页面文字透出，并适配 RTL 与系统减少动态效果设置。

### Windows 无线调试与文件管理

- “设置 → 传输与连接”加入无线 ADB 配对、重新连接及 Hinge 连接诊断导出。首次配对后复用本机 ADB 主机密钥；日志仅包含 Hinge 最近 256 KiB 连接诊断，不是完整 Android 系统 Logcat。使用时需要 Android Platform-Tools 和手机开启无线调试。
- Android 与 Windows 的“最近文件”过滤临时文件名中含 `temp` 或 `temporary` 标记的条目；过滤只影响列表显示，不会删除文件，也不改变手机存储目录浏览。
- Windows 侧边栏将“手机历史通知”简写为“通知”，页面标题与通知历史功能保持原样。

### 版本与验证

- Android：`1.4.14+117`，仅 `arm64-v8a`；Windows：`1.4.14.0`。
- Android Release APK 与 Windows Release 安装器、便携 ZIP 均由项目正式脚本构建；本次未运行自动化测试，也未进行 Android 真机无线 ADB、动画/字体或 Windows 安装后 UI 验收。
- APK 元数据确认包名 `com.hinge.office`、版本 `1.4.14+117`、ABI `arm64-v8a`；Android v2/v3 签名由正式构建脚本验证。Windows 安装器 FileVersion 为 `1.4.14.0`，便携 ZIP 含 `Hinge.exe` 和 `Hinge.Identity.msix`。未安装到真实设备或 Windows 环境验收。
- 发行资产及 SHA-256：

| 平台 | 资产 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| Android | `Hinge-Android-1.4.14.apk` | 21,159,073 bytes | `F8F21D69903076197B279B910C714B3F315905D9698B29E22808007C2C6618E1` |
| Windows | `Hinge-Setup.exe` | 198,233,163 bytes | `CCD03F7F142AA5D561D3FC2E306852E9EB952444AF5AFC3C7007BB5069CCE2B8` |
| Windows | `Hinge-Windows.zip` | 129,849,608 bytes | `07000A0B5330CBCDC6537C93E8438198DE37B6BDE4415B403810CB9E1ECE565E` |

## [1.4.9] - 2026-10-06

本节对照 GitHub 上一正式发行版 `v1.4.7`，只记录此后进入本次发行的变化；不重复 v1.4.7 已发布的 Android 传输记录和后台收件提醒修复。

### 最近文件过滤

- Android 与 Windows 的“最近文件”继续保留原有硬过滤：无扩展名文件不论大小均隐藏，且小于 10 KiB 的文件隐藏；此外叠加路径、文件名、扩展名、MIME 类型及大小等缓存特征评分，减少缓存和临时文件噪声。
- 过滤仅影响“最近文件”列表显示，不删除文件；从手机存储目录进入文件夹浏览时仍可查看这些文件。恰好 10 KiB 的文件不受大小硬规则影响。
- 修正 Android MediaStore 过滤后分页游标错位：按原始扫描条数推进分页，避免重复加载、跳过文件或提前结束。

### Windows 文件管理与关于页面

- 修复手机文件宫格缩略图只为每批前 80 项发起请求的问题；后续加载和窗口尺寸变化后也会为已显示图片补发请求，并继续使用有界并发，避免缩略图加载数量增长时无限占用网络或线程。
- 关于页面增加与应用版本同步的版本标识，并调整 GitHub 项目信息与主页链接的对齐布局。

### Android 短信通知去重

- 协调短信广播、短信数据库观察器及通知兼容来源：低优先级通知来源短暂等待更完整的短信事件，减少同一条验证码短信重复通知，并尽可能保留验证码通知的复制操作；兼容兜底仍有时限，不将短信正文或验证码持久化。

### 版本、资产与验证边界

- Android：`1.4.9+110`，仅 `arm64-v8a`；Windows：`1.4.9.0`。
- Flutter 静态分析通过，Flutter 测试 `72/72` 通过；Windows .NET 测试 `171/171` 通过。Android ARM64 Release APK、Windows Release 应用、右键菜单组件和安装器均构建成功。
- APK 包名为 `com.hinge.office`，元数据确认 `versionName=1.4.9`、`versionCode=110`、ABI 为 `arm64-v8a`；APK v2/v3 签名验证通过，且签名证书与上一 Android 候选版一致。Windows 安装器 FileVersion 为 `1.4.9.0`，便携 ZIP 包含应用入口及右键菜单身份包；安装器内嵌 ZIP 长度与便携 ZIP 一致。
- 自动化测试、静态检查和构建通过不等于现场验收；本次没有在 Android 真机上验证短信通知与最近文件显示，也没有安装 Windows 包检查实际界面和缩略图。
- 发行资产及 SHA-256：

| 平台 | 资产 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| Android | `Hinge-Android-1.4.9.apk` | 21,159,073 bytes | `0CBFFCB2BA1DB4E51D1A34C45667304E47E033F828A50F005F64321AE46F960E` |
| Windows | `Hinge-Setup.exe` | 198,222,866 bytes | `CC949E52AC609B0C02B69E233A70EB13B238A00D4C98B422AE35DC0C28242CA9` |
| Windows | `Hinge-Windows.zip` | 129,839,319 bytes | `16AF76DD6768621A976387A19A591F19F4D2D38D2950E13537B2DE7AE1AC0A12` |

## [1.4.7] - 2026-10-05

### Android 文件传输状态与后台收件提醒

- 原生 TCP 传输的重要生命周期结果（排队、完成、失败、文件接收完成/失败）现在由 Android 本地有界暂存；Flutter 在应用启动或回到前台时补写历史并确认，避免页面未监听或后台事件丢失后记录长期卡在中途百分比。
- Android 传输历史更新改由应用级事件订阅处理，不再依赖设备页面保持挂载；终态覆盖中途进度，迟到的排队/进度事件不会把“已完成”改回进行中。
- 接收文件经完整性校验并保存后，由原生服务直接发布系统通知；Flutter 同一事件仍可刷新通知，但只更新原通知、不重复弹出。保留既有通知权限与用户频道设置。
- 不改 TCP 协议、分块大小和重试策略；持久化只用于任务生命周期节点，不在文件分块循环中写历史或同步磁盘。

### 版本、下载与验证

- Android：`1.4.7+108`，仅 `arm64-v8a`。Windows 没有代码或版本变化，本次不重复发布 Windows 包；其版本仍为 `1.4.6.0`。
- Android Release APK 构建成功，Flutter 静态分析通过，APK 签名及包元数据核验通过。未运行自动化测试或 Android↔Windows 真机验收；构建通过不等于后台通知/历史补账已在所有设备上现场确认。
- 下载文件 SHA-256：

| 平台 | 文件 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| Android | `Hinge-Android-1.4.7.apk` | 21,159,073 bytes | `F71565A29A61CC28E6BCDA12FF9E89F5DF29D6188A74EA88DE5A21FD9F120C2A` |

## [1.4.6] - 2026-10-04

对照上一发行版 [`v1.4.5`](https://github.com/Chengeeker/Hinge/releases/tag/v1.4.5)，只列出之后的实际变化，不重复 1.4.5 已发布的 WebDAV、安装器和后台功耗优化内容。

### 多手机连接与文件管理

- Windows 可同时连接多台 Android 手机；文件管理只列出当前已连接设备，每台手机独立保留浏览状态、来源目录和预览/接收目标。
- 右键发送列出当前在线手机，并保留最近连接的一台离线可信手机供 Cloud Relay/待发送队列使用；不把其他历史设备混入菜单。
- 新增同名旧设备记录清理入口；清理需用户逐组确认，自动保留最新、在线和待发送目标，并先备份信任库。它不会合并设备身份或改动 Relay 配置、文件及传输历史。

### WebDAV、短信与连接提示

- 修复 WebDAV 配置了 Windows 挂载路径后，宫格图片/视频缩略图找不到原始路径的问题；缩略图映射仍限定在对应配置的挂载路径内。
- Android 在短信广播、短信库观察器与通知兼容转发共用的原生入口增加短时内存去重，避免同一条短信因多个兼容来源重复转发；去重指纹不写日志或磁盘。若用户开启通知历史，短信应用通知仍按现有通知历史设置保存。
- Android 连接失败回调到达时重新核对目标设备的已认证活动会话；若另一条并行/反向连接已经建立，则按实时会话状态显示连接成功，不再弹过期的失败横幅。真实设备网络竞争仍需现场验收。

### 版本、资产与验证边界

- Android：`1.4.6+107`，`arm64-v8a`；Windows：`1.4.6.0`。
- 本版构建 Android APK 与 Windows 安装器/便携 ZIP；构建成功不代表短信去重、并行连接竞争或多手机真机场景已完成现场验收。
- 发行资产及 SHA-256：

| 平台 | 资产 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| Android | `Hinge-Android-1.4.6.apk` | 21,159,073 bytes | `750FFC28049A08232D5E848A7A036D7870645CAA3EBCACA1CDA7EE4FB7215597` |
| Windows | `Hinge-Setup.exe` | 198,220,631 bytes | `B313E11326A42767E561757A04E6E21B99F209D808E9ECA8BFD58CE87A066ED3` |
| Windows | `Hinge-Windows.zip` | 129,837,084 bytes | `0F2C500D9FF7BDA2F17ECC72BFD96F50749D5E2FCB71CFAB83C74AEF26D4FCEA` |

## [1.4.5] - 2026-10-02

对照最近的 GitHub 发行版 [`v1.4.4-beta.1`](https://github.com/Chengeeker/Hinge/releases/tag/v1.4.4-beta.1)。该预发行版仅提供 Android 后台发现优化；本稳定版沿用该 Android 代码，不重复罗列其已发布的细节。本次新增与修复主要集中在 Windows WebDAV 和安装器。

### Windows WebDAV 文件管理

- 设置支持添加多个互相独立的 WebDAV 地址；每个地址分别保存 URL、用户名、密码、可选 UA、初始路径、备注、自签 HTTPS 信任开关和多线程传输开关。文件管理顶部以独立标签切换手机与各地址，标签拖动只调整顺序。
- WebDAV 浏览新增列表/宫格、按名称/类型/大小/修改日期排序、选择/全选、刷新、上传、下载、默认应用打开、新建目录和删除；宫格按可见项加载图片/视频缩略图，并在列表中展示修改时间、类型与大小。
- 修复 WebDAV 文件下载遇到 HTTP 301/302 等跳转时失败的问题。文件 GET 采用有上限的安全重定向处理；跨出配置目录或带签名查询参数时使用匿名请求，不把 WebDAV 凭据带到外部下载地址。
- 支持 Windows 文件与手机远端图片拖入 WebDAV，也支持 WebDAV 单文件拖至 Windows 文件夹。拖动时可将文件松到左侧取消区；取消会停止当前拖出下载，WebDAV 文件拖回 WebDAV 页面不会被重新上传。

### Windows 手机文件管理与安装器

- “最近文件”默认隐藏小于 10 KiB 的非目录项；该过滤不影响手机存储及其子目录，也不删除手机文件。
- 修复安装器在 Windows 11 右键菜单注册时可能无限等待的问题：对注册辅助进程增加等待上限，错误时明确提示并保留诊断日志；安装器会继续完成应用安装，不会自动反复重试或先卸载已有身份包。
- 修复同一产品版本的右键菜单身份包因内容变化而被 Windows 拒绝更新的问题；为打包身份与 Shell DLL 使用本次构建的独立版本标识，并验证注册后的实际版本与状态。

### 版本、资产与验证

- Android：`1.4.5+106`，`arm64-v8a`；Android 源码行为沿用 `v1.4.4-beta.1`，本次更新为稳定版版本号和签名发行包。
- Windows：`1.4.5.0`，提供 EXE 安装器和便携 ZIP。
- 本次 Release 构建了 Android APK 与 Windows 安装器/便携包；本轮未重新运行全量自动化测试，也未在真实 WebDAV 服务、Explorer 拖放和目标 Windows 安装环境完成运行验收。构建结果不等于这些现场场景均已验证。
- 发行资产及 SHA-256：

| 平台 | 资产 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| Android | `Hinge-Android-1.4.5.apk` | 21,159,073 bytes | `3558A8520913DC7536B1E0CC96FBAD9B6AA90F1EC5CF1D344927E1EF2D94F35C` |
| Windows | `Hinge-Setup.exe` | 198,205,972 bytes | `562ACC7BAD731A40876E73E73C9669C8649F815299D21C127007D660F233ECAC` |
| Windows | `Hinge-Windows.zip` | 129,822,424 bytes | `ABEE8B9FD2CDA638597690C0B33351B945910C118517290EBD1BFE6B3227926F` |

## [1.4.4-beta.1] - 2026-09-30 (预发行)

- 优化 Android 后台发现任务：应用进入后台后暂停 Flutter 侧重复 UDP 广播、子网探测与离线设备清理，并释放发现用组播锁；返回前台后恢复主动发现。
- 保留 UDP 监听、现有 TCP 会话和原生前台服务，降低改动对来件发现与文件传输连接的影响。
- 本预发行只提供 Android `arm64-v8a` 安装包；Windows 端未修改、未重新打包。自动化检查通过，实际后台耗电仍需在设备上观察验证。

## [1.4.3] - 2026-09-28

对照 GitHub 上一版公开发行版 [`v1.4.0`](https://github.com/Chengeeker/Hinge/releases/tag/v1.4.0)。以下只列出 `v1.4.0` 之后的变化；Cloud Relay 文件中转、Android 传输记录、Material 3 配色和剪贴板同步移除等已在 `v1.4.0` 发布的内容不重复列为新增。

### 连接状态与传输记录

- 修复 Android 页面晚于原生 TCP 会话启动时仍显示“已发现 · 可建立会话”的状态不同步；设备列表接入已恢复和新建会话，迟到的旧连接断开事件不会覆盖仍有效的新会话状态。
- Android 传输记录保存本机文件路径；点击记录使用系统默认应用/文件类型关联打开。目标文件已删除时显示明确提示，旧记录未保存路径时不会误报为文件已删除。
- “清空本机记录”只清除历史条目，不取消实际传输或删除文件；应用重启时将已不再活动的旧进行中条目标记为结束状态，保留仍由原生服务实际运行的传输。

### 局域网传输与可靠性

- 修复 Windows 右键发送队列对整个文件设置 25 秒硬超时的问题，避免慢速/大文件在接近完成时被取消并作为新任务反复重开；仍可由用户主动取消。
- Windows 发行默认文件数据块调整为 32 MiB；Android 接收端对应支持并在会话握手中公布 payload 上限。双方按较小能力协商，未公布上限的旧版对端仍按历史 16 MiB 协议上限兼容。32 MiB 是传输配置，不代表所有 Wi-Fi/手机都能获得固定提速；本次没有宣称慢速问题已在真机上根治。
- Windows 右键发送诊断记录实际选用 LAN、Cloud Relay 或待连接队列及单文件耗时；诊断写入失败和 Explorer 菜单刷新失败也记录到生命周期日志，便于区分通道、连接和发送阶段问题。

### Cloud Relay 进度与 Windows 设置

- 修复 Android Cloud Relay 乱序进度回调导致下载百分比回退跳动的问题；相同传输进度保持单调，上传/下载方向隔离，完成后重试可重新开始计数。Relay 文件传输本身已在 `v1.4.0` 存在，本项只修正进度表现。
- Windows 设置按用途整理为“个性化”“Windows 设置”“传输与连接”“Cloud Relay”合集；“关于应用”位于主导航设置入口下方，重复点击均可直接打开，Windows 通知仍留在 Windows 设置组。

### 版本、验证与资产

- Android：`1.4.3+104`，`arm64-v8a`；Windows：`1.4.3.0`。
- 自动化验证：Flutter 全量测试 `71/71`，Android 静态分析无问题；Windows .NET 测试 `95/95`。构建/自动化结果不等于用户设备间的真机验收。
- 发行资产及 SHA-256：

| 平台 | 资产 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| Android | `Hinge.apk` | 21,159,073 bytes | `9295E3A36C9B615297B019179DA480FBFD72D827A774AE5E37BE16A64F4D9739` |
| Windows | `Hinge-Setup.exe` | 198,115,306 bytes | `38BA0758DB26EE506CC13A06CD1F4EB8F37EEE7519068CF223132C42105C7D08` |
| Windows | `Hinge-Windows.zip` | 129,733,324 bytes | `AB196010FD5161A7A3B48D0563086A98B2C31EE350EF3F7E79378B410646AD5B` |

## [1.4.0] - 2026-09-25

对照上一个公开稳定版 [`v1.2.4`](https://github.com/Chengeeker/Hinge/releases/tag/v1.2.4)。本版重点完善 Cloud Relay 文件中转的可观察性与 Android 传输记录。Cloud Relay 文件中转此前已存在，本次不是首次引入；局域网仍优先，Relay 仍为用户自建、默认关闭的可选能力。

### Cloud Relay 文件传输

- Android 云上传/下载现在会在通知中心显示实时进度和已传输字节数，并展示准备、传输、校验、云端提交等阶段；上传完成后通知会明确显示“等待设备接收”，下载完成由文件接收通知接替。通知需允许 Hinge 的系统通知权限。
- 上传请求体与下载响应体按网络数据流更新进度，不再只在每个 8 MiB Relay 文件分块结束时跳变；异常时显示失败状态，客户端提示 HTTP 状态码，便于定位 Worker 响应问题。
- 修复 Android Cloud Relay 的启用状态、Worker 地址、设备 Token 和 Relay 加密密钥在应用重启后丢失的问题；部署管理员 Token 仍只用于注册，不作为客户端持久配置保存。
- Relay 仍使用用户自己的 Cloudflare Worker + 私有 R2；部署与网页版教程见 [Hinge-Relay 项目主页](https://github.com/Chengeeker/Hinge-Relay)。本版不提供 Hinge 托管云服务。

### 传输记录与连接体验

- Android 首页“设备操作”中的无效“文件管理”入口改为“文件传输记录”，本机持久保存最近 100 条 LAN/Cloud Relay 收发记录，展示方向、设备、状态、字节进度与时间；可删除已结束记录或批量清理，清理记录不会删除收到的文件。
- Windows 右键发送和 Android 原生待发送队列的任务纳入 Android 记录；断线自动重试的任务会显示等待/重试状态。原生队列当前没有取消接口，因此记录页不显示虚假的取消按钮。
- 修复 Android 断开设备时只关闭页面当前引用会话、遗漏已恢复会话的问题；断开操作现在关闭目标设备的所有活动会话。页面也会为进入前已恢复及之后新建的会话补挂传输事件。
- Android 的连接失败提示在自定义悬浮导航胶囊可见时移至其上方，避免挡住导航操作。

### Android 主题与剪贴板范围

- 预置主题色板改为标准 Material 3 `tonalSpot`；Android 壁纸动态颜色和独立的 Expressive 胶囊导航不变。
- 移除 Android/Windows 跨设备剪贴板同步、Shizuku 后台剪贴板监听，以及客户端 LAN/Cloud Relay 剪贴板同步路径。用户主动复制验证码、Relay 密钥等到本机系统剪贴板的功能仍保留。
- Cloud Relay 仅中转文件；客户端不再调用 `/v1/clipboard`。已部署的 Worker 不会因客户端更新自动改变；如需同步移除服务端旧路由，应单独更新并重新部署 [Hinge-Relay](https://github.com/Chengeeker/Hinge-Relay)。

### 版本、验证与限制

- Android：`1.4.0+99`，`arm64-v8a`；Windows：`1.4.0.0`。
- 发行资产：`Hinge.apk`、`Hinge-Setup.exe`、`Hinge-Windows.zip`。
- 自动化构建/测试不等于真机验收；Android 不承诺应用被系统完全停止后立即轮询 Cloud Relay。详见 [v1.4.0 GitHub 发行说明](https://github.com/Chengeeker/Hinge/releases/tag/v1.4.0)。

<details>
<summary>未公开的历史测试候选（不属于 v1.4.0 发行内容）</summary>

## [1.4.0] - 2026-09-24（Android 本地测试版）

### v1.4.0+99 预置主题色板切换为标准 Material 3

- 预置主题从 Material 3 Expressive 的 `expressive` 配色算法改用标准 Material 3 `tonalSpot`；Android 壁纸 Monet 动态颜色和独立的 Expressive 胶囊导航不变。
- Android `versionCode` 提升到 99；已生成可覆盖安装 +98 的 arm64-v8a 稳定签名测试包，尚未发布 GitHub Release。

### v1.4.0+98 移除跨设备剪贴板同步并增加 Android 文件传输记录

- Android、Windows 客户端移除剪贴板监听、跨设备 LAN 同步 UI/协议路径和相关后台组件；保留复制验证码、Relay 密钥等写入本机系统剪贴板的普通功能。
- Android 与 Windows 客户端移除 Cloud Relay 剪贴板信箱接口调用；独立 Hinge Relay Worker 源码同时移除 `/v1/clipboard` 新读写路由。已部署的 Worker 不会因本地改动自动更新，需用户之后重新部署当前仓库版本。
- Android 首页“设备操作”将无效的“文件管理”入口改为“文件传输记录”；本地保存最近 100 条 LAN/Cloud Relay 收发记录，显示状态及进行中的进度，可删除/清理已结束记录。Android 原生发送队列仍自动重试，记录页如实显示等待重试，不提供当前底层不支持的取消按钮。
- 文件传输、Cloud Relay 注册/R2 配置与上传下载流程保持保留；本次没有推送 GitHub 或部署 Worker。

### v1.4.0+97 Stellar UserService 启动诊断与非阻塞绑定

- Shizuku UserService 绑定请求移至后台线程，避免兼容层同步处理延迟时阻塞 Hinge 主界面及超时状态更新。
- Stellar 绑定失败时，Hinge 使用 Stellar 保留的 Shizuku 进程接口，只读获取本次启动区间的 `StellarUserServiceStarter` 系统日志，并在设置页展示；诊断不读取或记录剪贴板正文。
- 这是启动失败的诊断与响应性修复，不代表 Stellar 真机剪贴板同步已经恢复；需用手机确认是否显示“正在监听”并实际同步到电脑。

### Android Shizuku 后台剪贴板读取

- 新增默认关闭的 Shizuku 剪贴板读取选项；启用并授权后，由单独的 Shizuku UserService 监听系统纯文本剪贴板，再交给 Hinge 现有剪贴板去重、LAN 和 Cloud Relay 流程。
- 不申请悬浮窗权限，也不创建抢焦点窗口；关闭选项、关闭 Hinge 剪贴板总开关或 Shizuku 服务停止时，监听会停止或等待恢复。
- 需要 Android 10+ 与 Shizuku 11+；非 Root 无线调试启动的 Shizuku 通常需要在设备重启后重新启动。实际后台策略和剪贴板可读性仍需用户设备验收。

### 版本与验证边界

- Android 测试包版本：`1.4.0+90`，仅 arm64-v8a；Windows 客户端保持 `1.3.0.0`，本次没有修改跨端协议。
- Android AIDL/Kotlin 编译通过；此记录不代表 vivo 等真实设备已完成 Shizuku 授权、锁屏读取、LAN/Cloud Relay 端到端验收。

### v1.4.0+91 后台监听连接诊断

- Stellar 使用其 Shizuku 兼容层时，Hinge 仍通过 Shizuku API 绑定 UserService；不依赖当前未随 Stellar API 1.0.3 发布的 UserService SDK 类。
- 修复监听绑定或 `startWatching` 无回调时永久显示“正在连接”的状态问题：分别增加 15 秒超时、连接阶段状态和手动重连；真实手机端兼容性仍需测试。
- 测试版本提升至 `1.4.0+91`；Windows 版本及跨端协议不变。

### v1.4.0+92 Stellar UserService 连接兼容诊…11954 tokens truncated…剪贴板后台发送任务仍访问已释放连接的问题，避免未观察任务异常反复写入崩溃日志并导致常驻程序异常退出；
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

## 3.0.2

换机身份隔离、多品牌离线市场名、Windows 长设备标签与 M3 清空记录确认框。详见 [3.0.2 更新日志](docs/CHANGELOG_v3.0.2.md)。

## 3.1.0 预发行

- Windows 安装器会先核对已安装的右键菜单身份包，状态、安装目录及清单均一致时跳过重复注册，避免上次部署超时但实际完成后再次等待。
- Windows 关机期间暂停新的 ADB 进程启动，并记录会话结束通知；首页设备卡片的“文件管理”按钮调整深色模式下的描边绘制。
- 此版本供测试。关机弹窗修正尚未经过真实关机验收；Windows 自动崩溃的具体原因仍在调查，不能视为已修复。
- Android 全量单测有 6 项失败，发布构建与签名检查通过；详见本机开发记录。

- Windows 修复任务栏图标显示为灰色占位，补充 Shell 用于识别图标的窗口属性。
- Windows 在手机连接中断后尝试通过 BLE 唤醒，Android 接收后短时唤醒并恢复局域网会话。
- Android 返回前台时先验证旧连接。Windows 提供可手动开启的 10 分钟无线 ADB 辅助重连。


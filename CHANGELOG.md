# Changelog

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

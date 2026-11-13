# Hinge Windows Material 3 V4 视觉验收报告

日期：2026-10-10
结果：已完成 V4 代码修复、Windows Release 构建、180 项 Windows 测试及当前图形环境下的主要页面检查；完整 V4 验收仍为部分通过，未测的 DPI、二级设置页面和真实设备行为均标为 UNVERIFIED。

## 范围与回退

- 工作树锚点：D:\App\Hinge。保留已有 V2/V3 未提交改动；本轮只修改 Windows UI、主题资源及必要的 UI 状态刷新，没有改 Android 业务代码、传输协议、Shell 扩展或 Windows 系统集成。
- 本轮前快照：publish\rollback\pre-m3-v4-visual-repair-20261010\。包含源码工作树压缩包、源码清单、Windows 安装器/ZIP、Android APK 和开发文档副本。
- 源码快照：Hinge-source-v3-working-tree.zip，SHA-256 A2CC35850ED97443A1F7EE77D6CF2AD1047044F30A834DC42AB26DC560CC0CCA。
- 回退方法：需要恢复时，将该源码包解压到单独目录供对照/恢复，或按 source-manifest.txt 逐项恢复；Windows 与 Android 成品备份位于同目录的 windows/、android/。本轮未 reset/clean 工作树，也未提交、推送、安装候选包或更新公开 Release。

## 根因与改动摘要

### Gate 0 — 现状定位：PASS

- 首页内容根没有直接受 ScrollViewer.ViewportWidth 约束，内部栅格/纵向内容的测量宽度可能超过实际视口；首页宽屏/窄屏布局切换及历史列表可达性因此不稳定。
- 设置页外层分组宽度与行内部列布局脱节：行没有可靠 Stretch 到卡片可用宽度，尾部箭头受文案长度影响停在中间。
- 既有主题只有 tonalSpot 主色，普通 surface 角色呈紫调；没有可选择的 Windows 主题色预设。
- 确认 WinUI 3、现有 NavigationView、Grid、ScrollViewer、列表、控件样式和主题保存机制可继续复用，没有引入新的 UI 框架或重复的主题系统。

### Gate 1 — 首页与设置布局：UNVERIFIED（125% 窗口尺寸检查通过）

- 首页内容根绑定实际滚动视口宽度，并在视口变化时更新；桌面宽度使用响应式双列，窄窗切回单列，内容列有上限并保留垂直滚动。没有用裁剪隐藏越界内容。
- 设置分组行改为 Stretch，并使用 Auto,*,Auto 列；同组分隔线只画在行间。三个窗口宽度截图中箭头视觉上靠近同一分组右边缘。
- UI Automation 物理矩形观测（显示缩放 125%）：设置行控件右边距在 1537、1280、1024 物理像素宽窗口分别约 42、39、39 px；首页历史列表右边距约 63、64、64 px。内容均落在窗口边界内。行控件矩形已测，未单独自动读取每个箭头字形的边界，因此“同组箭头差异 ≤4 effective px”未作数值 PASS。
- 1024×768 与 1537×990 窄/宽截图及 UI Automation 的 ScrollItemPattern 显示最后历史项可滚动到视口内。1024 设置页底部截图曾被自动化导航带到首页，故不能把该截图作为设置长页尾部的图像证据。
- 已验证窗口物理尺寸：1537×990、1280×800、1024×768；DPI 120（125%）。换算的窗口尺寸约为 1229.6×792、1024×640、819.2×614.4 effective px（物理窗口矩形除以 1.25）。Windows 100%/150% 缩放未测，故整个 Gate 标为 UNVERIFIED。

### Gate 2 — 六种主题与持久化：UNVERIFIED（主题矩阵与重启恢复已验证）

- 使用应用既有设置存储，仅增加/复用 WindowPalette；Light/Dark/System 模式仍独立。新用户默认 Sage；原 tonalSpot 偏好映射至 Purple，避免把既有紫色用户选择静默改成绿色。
- 六个预设 Sage、Blue、Purple、Teal、Orange、Neutral 均有完整 Light/Dark ARGB 语义角色表，来自 Android ColorScheme.fromSeed 的 tonalSpot 导出；Windows 运行时不引入 Flutter 引擎或动态猜色公式。
- 实际切换并截图检查了六种预设的 Light/Dark 共 12 种组合；退出后再启动检查 Neutral + Dark 恢复，随后恢复 Sage + System。设置、首页和 WinUI 页面共享主题资源刷新。
- 实际首次安装/无偏好用户的首启默认过程未重置配置来做破坏性测试；Windows High Contrast 未切换验收。因此这些场景仍为 UNVERIFIED。

### Gate 3 — 语义排版和组件：UNVERIFIED（代码与主要页面可视检查完成）

- App.xaml 与 HingeM3.xaml 提供共享语义文字/表面/状态资源；主要 XAML 页与 C# 动态文本改用共享资源。普通分组使用低强调 surface container，主色用于选中及交互强调。
- 设置页按“类目标题在组外、一个共享表面、图文行和内缩分隔线”的结构对照目标图；没有只改主题色或加大圆角。
- 信息提示图标此前受 WinUI 默认 severity 蓝色影响；本轮对 Informational 使用明确的 Segoe MDL2 Info 图标和主题色，其他 severity 继续由 WinUI 提供语义图标。见 Microsoft WinUI InfoBar 资源规范与 Segoe MDL2 图标表：
  https://github.com/microsoft/microsoft-ui-xaml-specs/blob/master/active/InformationBar/InformationBar.md
  https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-ui-symbol-font
- 尚未通过 UI Automation 汇总页面标题、组标题、行标题、说明、按钮/元数据的实际运行时 FontSize、FontWeight、FontFamily 表。语义样式已静态核对并查看主要截图，但按 V4 技术规范该项不作完整 PASS。

## Gate 4 — 页面截图对照：UNVERIFIED（列出的页面已实际启动并截图）

环境为 2560×1440 显示器、Windows 缩放 125%、应用 Sage + System 浅色模式。下面截图是构建后的真实 WinUI 窗口捕获，窗口物理尺寸写在文件名中；截图保存在临时验收目录，没有把可能包含设备/文件信息的原始 PNG 加入 Git。

证据目录：D:\App\Hinge\tmp\m3-v4-evidence\

- 设置页：v4-final-settings-1537x990-125.png、v4-final-settings-1280x800-125.png、v4-final-settings-1024x768-125.png、v4-final-settings-1024x768-top-125.png。已与目标图的分组、文字层级、表面明度、分隔线和行比例作视觉比对。
- 首页与最后一条记录：v4-final-home-1537x990-125.png、v4-final-home-1280x800-125.png、v4-final-home-1024x768-125.png、v4-final-home-last-history-1537x990-125.png、v4-final-home-last-history-1024x768-125.png。
- 文件管理与日历：v4-final-files-1537x990-125.png、v4-final-calendar-1537x990-125.png。
- 笔记、待办、相册、通知：v4-final-notes-1537x990-125.png、v4-final-todo-1537x990-125.png、v4-final-photos-1537x990-125.png、v4-final-notifications-1537x990-125.png。
- 个性化、Windows 设置、关于：v4-final-personalization-1537x990-125.png、v4-final-windows-settings-1537x990-125.png、v4-final-about-1537x990-125.png；关于页另有 v4-final-about-1024x768-125.png。
- 六色 Light/Dark 矩阵：v4-final-palette-{sage,blue,purple,teal,orange,neutral}-{light,dark}-1537x990-125.png。v4-persistence-after-restart.png 记录重启恢复。
- 传输设置、Cloud Relay、WebDAV 二级页及带真实连接数据的文件列表未保存安全截图；不得据此声称其视觉验收通过。上述页面的密钥、服务器地址、账号字段可能含私有信息，未把其内容复制到报告。

## Gate 5 — 构建、回归与实际功能：UNVERIFIED（自动构建/测试通过，设备集成未验）

- Windows Release：dotnet build windows\Hinge.sln --configuration Release -m:1 -nodeReuse:false，0 warnings、0 errors。
- Windows 回归：dotnet test windows\Hinge.sln --configuration Release --no-build -m:1 -nodeReuse:false，180/180 通过。
- 已从构建输出实际启动候选程序，打开主要页面、个性化页、Windows 设置和关于页；主题预设切换及重启持久化已实际操作。未对传输、清理、删除、连接、拖放等会改变数据或访问外部设备的操作做端到端验证。
- 按项目规程重建了 Android 主线签名 APK；Android 源码无改动。publish\Hinge.apk：com.hinge.office，1.4.14+119，21,159,073 bytes；SHA-256 B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3。v2/v3 签名与稳定证书校验通过。没有安装到 Android 设备。
- 当前没有可用连接手机；文件发送/接收、通知/照片实际数据、BLE、Shell 菜单、Cloud Relay、WebDAV 及 Windows High Contrast/100%/150% DPI 场景均未实机验收。测试和构建证据不能替代这些现场验收。

## 最终判定

没有发现已知的 Release 构建或现有 180 项测试失败；本轮修复工作和主要页面真实截图已完成。按照 V4 规定，“缺少截图/真实场景则不能标视觉 PASS”，因此 Gate 1–5 整体仍是 UNVERIFIED，只有 Gate 0 为 PASS；这不是完整验收通过。当前可回退快照完整，工作树未提交或推送。


## 补充：手机品牌标识适配（2026-10-10）

- 追问后复查发现：既有品牌 SVG 只用于首页主设备区已选择的远端手机；设备发现列表仍全部显示通用手机图标，传输历史没有图标，文件管理手机标签也只有文字。这是适配范围遗漏。首页主设备当前展示本机服务时仍用通用设备图标，这是 Windows 本机身份，不应冒充手机品牌。
- 复用既有 SVG 和 ResolveBrandAsset 映射，识别 Honor、vivo、Xiaomi/Redmi/POCO、Samsung/Galaxy、Huawei、OPPO；用设备制造商、型号和显示名称匹配。补到首页附近设备行、传输记录和文件管理手机标签；未知品牌仍回退通用手机图标，不新增协议或持久化数据。
- 新构建候选实际截图 brand-final-home-1537x990-125.png 中，四条 vivo X300 Pro 传输记录均显示蓝色 vivo 标志；brand-final-files-1537x990-125.png 确认文件管理无异常。现场附近设备列表为空，文件管理只有“手机（未连接）”标签，所以真实手机行和带品牌手机标签尚未现场显示验证。
- 补充后 Windows Release 0 警告/0 错误、Windows 测试 180/180 通过；Android Release APK 按项目规则重新构建，源码无改动，SHA-256 仍为 B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3。
## Windows 安装包补充（2026-10-10）

- 品牌标识修复后的当前工作树通过 `scripts\build_release_windows.ps1` 生成 Windows 1.4.14 安装包；WinUI 发布、原生 Shell 扩展、身份包及安装器均构建成功，MSBuild 0 警告/0 错误。
- `publish\windows\Hinge-Setup.exe`：198,266,882 bytes，SHA-256 `B14CBB396A275CFF794808889DF031388E084BE584E8D1CA6969962A70C0806E`，FileVersion `1.4.14.0`。`Hinge-Windows.zip`：129,883,327 bytes，SHA-256 `B37F8D6A78430240E72ABDB5D26027BEA8530A5DEAAC266161B83E759F2D5EEF`。
- ZIP 807 项全部解压读取，未发现条目长度错误；含 `Hinge.exe` 和 6 个手机品牌 SVG；ZIP 内 `Hinge.dll` SHA-256 `E66F271107F5A78C76A1B2822E491EF888886E9BCD435830C5141DF290D98F84` 与本次发布输出一致。安装器尾部载荷标记、长度及 SHA-256 均与 ZIP 相符。
- 覆盖前的安装器/ZIP 备份：`publish\rollback\pre-v4-installer-rebuild-20261010\windows\`，原安装器 SHA-256 `6452FD2A4098E3618837383BC62D4E04E10CA673DBE7FE067DFE3EB23DF4DF1E`，原 ZIP SHA-256 `F5A3021098A0EA62B1729315E05C22347BA78C82FE03829E9B3C12C0ADDFD237`。
- EXE Authenticode 状态为 `NotSigned`；本轮只构建和校验安装包，未安装或启动它。此前 Windows 自动化测试与品牌页面截图证据见第 304 节；真实手机设备列表仍需连接设备验收。
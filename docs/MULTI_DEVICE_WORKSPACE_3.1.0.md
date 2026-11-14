# Hinge 3.1.0：多设备工作空间改造说明

## 目标
Windows 同时连接多台 Android 手机，不再把“第一台在线手机”作为所有页面的隐式数据源。所有业务写操作均应在执行时明确到 DeviceId，设备离线不可重定向到其他手机在线会话。

## 已实施
- DeviceWorkspaceManager：显式当前工作设备、页面独立设备和设备维度缓存键；保存当前设备到本机设置。
- Windows 导航栏设备选择、首页已连接设备卡片；文件管理保留自己的独立设备标签。
- 笔记、待办、日历、相册加载使用选中的会话；消除离线时回退缓存旧会话的风险。照片数据带 SourceDeviceId。
- 通知历史同时读取多台已连接手机，按来源筛选、按时间排序、分页及去重。通知删除/打开按源设备发送，清空全部需确认。
- 实时通知以设备隔离去重，并把通知操作路由回对应手机；Windows 通知标题展示设备名称，旧式没有来源的操作仅在单台手机在线时允许。
- 电脑发文件/文字需要明确指定一台或多台手机；传输记录按实际接收手机署名。
- 手机内文件右键复制可以粘贴到另一台手机：先通过 Windows 私有临时目录接收，再向目标手机指定存储文件夹发送，完成后清理中转文件，目标手机明确确认。
- 多路 scrcpy 投屏会话按 DeviceId 独立管理；Android DUMP 权限保护下的诊断接口 `/identity` 返回伪名设备 ID，Windows 通过无线 ADB 指定 transport 验证身份后才启动投屏，未匹配的设备绝不误用。
- 剪贴板同步以当前工作手机为唯一桥接目标，并按设备单独 opt-in；切换设备时关闭旧桥接，再验证新手机。
- 应用及安装包更新至 Windows 3.1.0 / Android versionName 3.1.0、versionCode 213。

## 设计和安全约束
- 全局工作设备只决定内容区默认目标。文件管理器的设备标签是页面局部状态，不能影响其他业务模块。
- 同名手机不展示唯一标识符，但路由与缓存使用设备内部 ID。
- 手机离线时不自动改写另一手机数据；多目标发送必须由用户勾选。
- 同一通知 ID、笔记 ID、照片 URI 在不同手机间不可复用为全局唯一键。
- Android DUMP 权限限制了 ADB 设备标识接口访问；该接口不输出 Android_ID 原值或密钥。
- 同时连接多台手机与同时投屏是不同层面的能力；每台手机仍需要有效无线 ADB。
- 跨手机传输经过 Windows 私有暂存空间，不把中转文件留在用户下载目录。

## 实测和局限
- Windows 回归测试包括之前的并发连接与新工作设备切换、通知按源路由策略。
- Android 的设备标识测试和 Release 构建验证仍需使用签名包及真机复核。
- 两台真实手机同时投屏、同时编辑数据、断网重连及跨机大文件压力测试需在两台同时接入时再做。
- 同型号手机的展示名称可相同，但内部 DeviceId 不相同。
- 剪贴板遵循单 Windows 系统剪贴板、单当前工作手机同步设计，不做多手机后台广播同步。
- 本次默认屏幕编码、码率仍为全局偏好；每台手机投屏进程、目标验证和停止按钮已独立。

## 常用命令
Windows:
```powershell
dotnet test windows/Hinge.Tests/Hinge.Tests.csproj --no-restore
dotnet build windows/Hinge.App/Hinge.App.csproj --no-restore
pwsh scripts/build_release_windows.ps1 -TestBuild -TestBuildTag multi-device-workspace-213
```

Android (Gradle 内存提高，避免大型 Compose Debug 测试引发 Kotlin OOM):
```powershell
./gradlew :app:assembleRelease :app:testDebugUnitTest --tests com.hinge.office.HingeDeviceIdentityTest --offline --no-daemon '-Dorg.gradle.jvmargs=-Xmx5G -Dfile.encoding=UTF-8' '-Pkotlin.incremental=false'
```

正式下载及安装包只在项目的 `publish` 目录展示最新版本；历史文件保留于 `BackupRelease`。

## 首页与设备选择的 M3 界面调整（3.1.0 预发布迭代）
- 工作设备下拉框只显示已认证且在线的 Android 手机，以及用户明确选中的唯一离线手机；信任库中的历史重复机型不再铺满列表，信任数据本身不做破坏性删除。
- 同名的真实在线手机按内部 DeviceId 保持独立，避免用机型名错误合并不同物理设备。
- 首页移除单机 Hero 与本机服务卡片的可见布局，改为响应式“我的设备 / 附近设备”并排布局，窄窗纵向排列；每台设备采用 M3 surface-container-low 分层卡片、语义化按钮与适度圆角。
- 每台已连接手机均可直接点击“发送文件”，发送方法捕获明确 DeviceId，并在选择文件后重新验证连接，不依赖当前工作设备、也不向其他设备回退；文件管理标签仍与全局工作设备独立。
- 附近列表展示时排除本机身份与非 Android 的 `local` 占位项，并隐藏地址列表里的 `local` 文本；仅影响 UI，底层发现、会话和连接地址完整保留。
- 本机名称、设备 ID 和 UDP/TCP 服务端口已挪到“设置 → 传输与连接 → 本机服务”。
- 新增历史记录过滤与 `local` 展示层回归测试。

## 首页 Grid 排版与发现列表旧身份修复（2026-11-14）
- 将此前隐藏但仍占据 `HomeSummaryGrid` 的 `IdentityCard` 与 `ServiceCard` 移至独立的零尺寸隐藏容器，保证旧字段与事件绑定兼容，同时从可见网格彻底脱离。
- 首页可见网格只有“我的设备”和“附近设备”两个 Border：桌面同一行、窄窗口两行；取消双行跨越，RowSpacing 仅在窄窗口生效，消除空白层与传输记录错位。
- `NearbyDevicePresentation.Visible` 仅在展示层过滤同名同厂商同型号的旧离线身份（前提是另一 ID 正在线或可发现）；在线的同型号设备始终保留两个独立 ID，不更改发现注册表、可信设备资料、连接或路由。
- 当前工作设备标签、内层设备卡片及 Logo 底板全部从 `HingeCornerRadius` 读取应用统一圆角；移除单独写死的 100/16/12 圆角值。
- 新增 6 项设备展示层回归测试；Windows 当前共 378 项通过，正式安装包重新构建并覆盖安装到 `D:\Hinge`。
- 实际 Windows UIA 运行时检查：左右栏标题 Y 坐标一致，传输记录位于设备栏下方，首页有正确的按钮与设备内容。屏幕位图捕获在 WinUI 合成层存在空白限制，UIA 结构与布局检查有效，仍建议使用者肉眼验收最终视觉。

## 首页设备卡片：文件管理跳转与 M3 选中配色（2026-11-14）

- 修复首页已连接手机卡片的“文件管理”按钮：原实现直接调用 `ShowFileManagement()`，导致页面已经切换但左侧 `NavigationView` 仍选中“首页”。
- 新入口 `OpenPhoneFilesFromHome(deviceId)` 先选中侧边栏 `Tag=files` 对应的导航项，由正常 SelectionChanged 路径打开文件管理，再将该卡片的 `DeviceId` 应用到文件管理的手机标签。不会为首页叠加独立的文件管理页，也不会修改全局当前工作设备。
- 视觉调整：将已选中设备卡片的深色主强调边框替换为 M3 `OutlineVariant`；按实际明暗主题获取 `SurfaceContainerLow` 与 `SurfaceContainer` 背景，徽标底板使用 `SurfaceContainerLowest`。当前工作设备标签改为 `PrimaryContainer` / `OnPrimaryContainer` 色彩组合，圆角继续使用 `HingeCornerRadius`。
- Windows 378 项自动化测试通过，正式安装包构建和覆盖安装成功。
- 实际 Windows UI Automation 验收：点击 OPPO 卡片“文件管理”后，`Navigation.files` 处于已选中状态、`Navigation.home` 未选中，`FileManagementPage` 与对应手机 TabItem 存在；再次选择首页后导航状态正确恢复。
- 此为 3.1.0 正式版前的本地更新；既有 GitHub `v3.1.0-pre2` 发布文件与标签不在本次改动范围内。

## 最近文件的 OPPO/vivo 厂商缓存过滤（2026-11-14）

基于 OPPO Find X9s Pro 与 vivo X300 Pro 实机近期文件抽样、路径复核，新增以下高置信度生成目录的过滤规则。**仅应用于「文件管理 → 最近的文件」；正常的「手机存储」目录浏览、下载、复制、删除不受影响，也不删除手机上的任何文件。**

| 厂商 | 目录或精确路径 | 依据 |
|---|---|---|
| OPPO | `oua_classifier/` | `.cache.bin`、`aiboost_gpu_cache_*.bin` |
| OPPO | `Download/.content_portal/` | `.portal_background*.png` 系统资源 |
| OPPO | `Download/.OplusDragAndDrop/` | `OPLUSDRAG_*.png` 跨应用拖拽中间文件 |
| OPPO | `.MediaTrash/` | 媒体回收站副本，应通过回收站恢复而非作为普通最近文件 |
| vivo | `.yuetu_unlock/` | `PictureUnlock_*.pictureunlock.jpg` 和 `vivo_ad/real/*.0` |
| vivo | `.vivoBrowser/img/` | 浏览器内部哈希图片资源 |
| vivo | `Download/.TempDragFile/` | vivo 临时拖拽资源（即使扩展名为 PNG） |
| vivo | `Download/vivo办公套件/log.txt` | 特定办公套件诊断日志，其他 `log.txt` 仍保留 |

规则严格匹配目录段，不依赖泛化文件名，支持全路径、相对路径、正反斜线与大小写规范化。Windows `RecentFileFilterRules` 和 Android `RecentGeneratedPaths` 保持同一组规则；高置信度路径不受公用目录、已知 MIME 类型的扣分影响。Android 在查询结果整理阶段先筛，Windows 在收到结果后复核。

**明确不排除**：`Download/阅读备份/backup.zip`、`Documents/YiYuJiZhang/*.penny`、`Pictures/.2041841469/sample_0_20.webp`、用途未确认的 `privacy.db`、普通 APK/ZIP、用户照片、相似但不完全相同的目录名。这些文件可能具有价值或用途未证实，先不纳入排除名单。

此筛选只提升最近文件的信噪比；系统缓存的实际磁盘占用不会因隐藏而改变。安装包和原有 `v3.1.0-pre2` 预发行版互不覆盖。

## 侧边栏收起时的当前工作设备选择器（2026-11-14）

- 修复 `NavigationView` 收起状态下，`PaneFooter` 的“当前工作设备”标题被强制断行、`ComboBox` 被裁切的问题。
- 展开态继续使用原机型列表；收起态完全隐藏文字/下拉框，以 `48×48` 的手机图标按钮代替，与侧边栏图标水平居中对齐，并使用 `HingeCornerRadius`（12）、`HingeM3PrimaryContainerBrush` / `HingeM3OnPrimaryContainerBrush`。
- 点击图标动态从 `GetPhoneTargets()` 生成 `MenuFlyout`：按唯一 `DeviceId` 展示当前可选设备及连接状态，当前选中项打勾；菜单切换复用 `SelectWorkspaceDevice` 的原路由及持久化逻辑，不创建另一份选择状态。
- 折叠按钮的工具提示及无障碍名称显示实际当前机型及在线/离线状态；在窗口初始化、PaneOpened、PaneClosed、DisplayModeChanged 时切换两套 Footer 的 `Visibility`，避免在窄侧栏测量完整下拉框。
- Windows 原有 398 项测试全部通过，WinUI 客户端构建为 0 错误；安装包和便携版已重新构建并安装在 `D:\Hinge`。
- 真实 Windows UI Automation 检查：收起按钮中心 X 与首页导航图标中心 X 的偏差为 **0 px**；实际点击手机按钮出现对应 OPPO 机型菜单且能选中；打开导航后紧凑图标隐藏、完整 ComboBox 显示，重新收起后状态正确恢复。
- 只改 Windows 界面，Android 仍是 **3.1.0（214）**；GitHub 已存在的 `v3.1.0-pre2` Release 不被修改。

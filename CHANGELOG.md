# Changelog

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

# 发布流程

本文是 Hinge 当前的可执行发布流程。发布前应先看 `docs/development.md` 的环境和验证说明。

## 操作前检查

任何修改、构建、签名、压缩、复制安装包或发布操作前，先完整审阅本文和 `docs/development.md`；完成后把实际版本、产物路径、校验结果和仍需验收的边界同步回文档。不要以旧的命令输出或旧发行版资产代替本次验证。

## 版本约定

- Android 应用版本来自 `android/pubspec.yaml`，当前可交付版本为 `1.1.7+47`；
- Android 和 Windows 的产品版本名保持为 `1.1.7`；后续 GitHub 标签使用稳定版本号（例如 `v1.1.7`），Release 直接标记为 `Latest`，不使用 `-dev`、`-pre` 后缀或 Pre-release 标签；
- Windows 发布只提供自包含 EXE 安装器和便携 ZIP，不单独提供 MSIX 或 CER 资产；EXE 安装负载内部允许携带签名稀疏身份组件及原生 Shell DLL。
- EXE 安装器启用 .NET 单文件压缩；安装逻辑不变，发布前仍需验证安装、更新和目录选择。
- EXE 安装器更新稀疏包前会先停止当前用户会话的 Explorer，释放旧 Shell COM 宿主后重试卸载旧包，再用 `-ForceApplicationShutdown` 和 `-ForceUpdateFromAnyVersion` 注册当前包并校验 `Status=Ok`；最后无论注册成功或失败都会确保 Explorer 恢复。刷新只在注册校验成功后视为完成，失败时记录 `shell-integration-error.log`，主程序仍可通过兼容菜单运行。
- EXE 安装版的 Windows 11 资源管理器“通过 Hinge 发送到”由签名稀疏包身份注册原生 `IExplorerCommand`，悬停后只读取 Hinge 写入的已连接设备快照并动态列出机型；多选路径由 Shell 扩展通过当前用户专用命名管道交给 Hinge 单实例，再复用现有文件发送链路，目标为 Android `Download/Hinge` 自动分类目录；Hinge 未运行时才回退到带参数启动。便携版没有可信包身份，使用“显示更多选项”中的旧式菜单回退。

## 发布前检查

```powershell
git status --short
flutter analyze --no-pub
flutter test --no-pub
dotnet build windows/Hinge.sln --configuration Release --no-restore
dotnet test windows/Hinge.sln --configuration Release --no-build --no-restore
git diff --check
```

本次日历界面改动的专项验证还必须覆盖：自定义标题栏下 `NavigationView` 不再分配固定 Header 行，左侧/最小导航模式不再保留模板大块顶部 Margin，所有侧边栏页面只保留统一的 16px 内容顶部间距；浅色/深色主题下所有动态文字、日期格 PointerOver/Pressed 状态及系统标题栏按钮可读，缓存的相册和通知历史在主题变化时重绘；首页深色模式下在线设备使用高对比度强调文字，成功状态条使用深色语义表面且标题、正文清晰可读，切换主题后立即重绘；月份切换按钮图标居中；Android 厂商日历在丰富投影失败时仍能通过 `Instances` + `Events` 分批补全事件来源和生日类型；单日全天生日不得扩展到次日，内部 `ExtendedProperties` 不得出现在 Windows 详情中。vivo“小V建议”不在标准日历提供器时，最近短信或 Hinge 私有通知历史中同时具备日期、时间和车次号的内容必须生成去重后的临时出行项，且不得传输原始消息正文。

Windows 常驻稳定性专项验证还必须覆盖：设备主动断开、网络瞬断和应用退出时，身份确认、心跳响应及剪贴板后台发送任务不产生新的 `UnobservedTaskException`；右键扩展向正在运行的 Hinge 发送请求后 `dllhost.exe` 不得出现 `MoAppHang`，命名管道以客户端关闭句柄作为消息结束标志，禁止恢复 `FlushFileBuffers`。

## 最近一次本地构建记录

- 2026-09-14，版本 `1.1.7+47`：Android arm64 APK 构建、签名和脚本校验成功；Windows EXE 安装器与便携 ZIP 构建成功；Windows 常驻连接发送竞态与资源管理器命名管道死锁修复已编译进包。
- `publish/Hinge.apk` SHA-256：`90823AA3D4A1CA766D728182475300E9F2C77D07A70E124BEC982D5F332C62E7`，20,188,321 字节；
- `publish/windows/Hinge-Setup.exe` SHA-256：`D706972A096328B0A63621FDDC0D65A9496DD6AB65FBECBED60959AF1F1638C6`，201,442,831 字节；
- `publish/windows/Hinge-Windows.zip` SHA-256：`AC341221EDFBA72EA8133DA45518864399110CBECF490EAADCCB07370FF4E156`，129,675,756 字节。

确认以下内容没有进入 Git：

- Android 签名库、密码和临时 PKCS12；
- `publish/`、`tmp/`、`bin/`、`obj/`；
- 本机日志、崩溃转储和包含个人设备信息的测试文件。

## 构建

```powershell
Copy-Item android/android/key.properties.example android/android/key.properties
# 只需第一次编辑 android/android/key.properties，填写本机签名信息
.\scripts\build_release_android.ps1
.\scripts\build_release_windows.ps1
```

Android 脚本会读取被 Git 忽略的 `android/android/key.properties`，优先使用其中的签名库路径、类型、别名和密码。脚本使用已验证的 JDK 17 构建，并将 BKS 临时转换为 PKCS12 供 `apksigner` 使用。路径、别名、密码、签名库和临时副本不会进入仓库或日志；缺少签名输入时脚本直接失败，不生成可误装的调试签名更新包。CI 或临时构建仍可用进程环境变量覆盖本地配置。

Android 发布只使用 `android-arm64`，脚本在复制前检查 APK 只包含 `arm64-v8a` 并验证 APK 签名证书。唯一的 Android 本地发布产物是 `publish/Hinge.apk`；Flutter 的 `android/build/app/outputs/flutter-apk/app-release.apk` 仅为中间产物。

新 APK 复制成功后，脚本才会清理旧的 `publish/android/Hinge.apk`；签名失败或复制失败时不会删除旧包。

压缩由构建脚本负责：Windows 脚本负责单文件 EXE 压缩和便携 ZIP，Android/Flutter 负责 APK 的构建压缩与裁剪。APK 签名后禁止再次手工压缩或重打包，以免破坏签名；新增自动压缩步骤必须同时更新开发文档。

构建结束后检查：

- `publish/Hinge.apk` 存在且签名验证通过；
- `publish/windows/Hinge-Setup.exe` 可以选择安装目录；
- `publish/windows/Hinge-Windows.zip` 解压后可直接启动；
- EXE 首次安装能够请求一次管理员确认并把 Hinge 公钥证书加入 LocalMachine TrustedPeople，随后成功注册 `Hinge.Office.Identity` 稀疏包；
- 稀疏包注册成功后，开始菜单和桌面快捷方式的 AppUserModelId 必须为 `Hinge.Office.Identity_29ecp0hep5z68!Hinge`；包内 44×44、150×150、50×50 及各档 `targetsize` / `altform-unplated` 图标必须具有与文件名匹配的真实像素尺寸；
- 确认发行版资产只有 APK、EXE 和便携 ZIP，并分别记录 SHA-256。

## GitHub Release

1. 提交源码、协议和文档，不提交 `publish/` 二进制目录；
2. 推送 `main` 和稳定版本标签（例如 `v1.1.7`）；
3. 创建 GitHub Release 并直接标记为 `Latest`；不再创建 Pre-release，除非明确发布需要保留的历史测试快照；
4. 上传 `publish/Hinge.apk`、`publish/windows/Hinge-Setup.exe` 和 `publish/windows/Hinge-Windows.zip`；
5. 使用 `CHANGELOG.md` 生成发布说明，并在发布页注明真机验收边界；
6. 发布后验证源代码提交、tag、Release 资产数量和文件 SHA-256。

## 安装器选择

EXE 是普通用户的推荐路径：它是自包含安装器，安装时选择目录，并负责建立 Windows 11 第一层右键菜单所需的可信稀疏身份。便携 ZIP 适合不希望写入安装目录或需要直接解压运行的场景，但它不会自动导入公钥或注册包身份，资源管理器菜单使用兼容模式。

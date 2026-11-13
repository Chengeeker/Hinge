# Hinge 安装器默认目录误指向项目 Debug 输出修复（2026-10-11）

## 根因

安装器 `FindExistingInstallPath()` 把正在运行的 `Hinge.exe` 放在最高优先级。本机运行实例来自 `D:\App\Hinge\windows\Hinge.App\bin\Debug\net8.0-windows10.0.19041.0\win-x64`；开始菜单/桌面快捷方式、注册表 `Software\Hinge` 与 `%ProgramData%\Hinge\install-location.txt` 也已指向该 Debug 输出。安装器因此把项目构建目录当成旧安装目录。旧的 `D:\Hinge` 仍有 1.4.14.0 的 Hinge.exe，但原检测未搜索磁盘根目录。

## 修复

- 安装器先读取持久安装记录与快捷方式，再参考运行进程，避免 Debug 实例抢占检测结果。
- 路径如果处于含 `.git` 标记的源代码工作树下，就不会被接受为已安装目录（兼容 `.git` 文件形式的 worktree）。
- 兼容旧版按磁盘根目录安装的习惯，扫描固定盘的 `X:\Hinge` 目录作为回退候选；本机因此重新识别 `D:\Hinge`。
- 安装完成后的注册表、ProgramData 位置记录和快捷方式仍沿用同一套写入路径；未改应用业务逻辑。

## 验证

- Windows 测试：183/183 通过（新增 3 个路径策略断言，覆盖普通工作树、worktree `.git` 文件与独立安装目录）。
- Windows Release 安装器和便携 ZIP 成功重建，Shell Extension 构建 0 warning/0 error。安装器 UI Automation 实测安装路径输入框默认值为 `D:\Hinge`；仅打开并关闭安装器，没有执行安装、覆盖文件或关闭现有 Debug 实例。
- Android 主线 APK 按项目规程重建（Android 源码未改），Release 脚本签名验证成功，v2/v3 均有效。
- 当前本机现有快捷方式、注册表和正在运行的应用路径仍指向 Debug 目录；本次只修复了之后运行的新安装器，不在未获明确安装请求时替用户执行覆盖安装。新安装器成功安装到 `D:\Hinge` 后会按原流程更新这些指向。

## 产物与回退

- `publish/windows/Hinge-Setup.exe`：203,878,639 bytes，SHA-256 `596BFEDC10B9213637417AC590289AB1CAD87AFFB122FAEEFBDA3AACBED9B13C`，Authenticode `NotSigned`。
- `publish/windows/Hinge-Windows.zip`：135,494,822 bytes，809 项，SHA-256 `46D9B3009967164F34CBB6C98A6A8294861221ED546B135C3B2987FAA70BD1CF`。
- `publish/Hinge.apk`：21,159,073 bytes，SHA-256 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`。
- 修改前快照：`publish/rollback/pre-installer-debug-path-detection-20261011/`，包含旧安装器/ZIP、`installer/Program.cs`、原开发文档与测试项目文件。新增路径策略源码和测试若需回退应一并删除。

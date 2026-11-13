# Hinge Windows M3 当前基线（2026-10-11）

## 基线定义

- 基线取自本地工作树 `D:\App\Hinge`，Git HEAD：`860362396055cf49a4a0929ba5a51865530af3ee`，并包含本地未提交的 M3 页面、布局修复和安装器路径修复。当前工作树有多项既有修改；它们均属于本次基线，不能用 `git reset` 或清理构建来回退。
- 当前 Windows 客户端仍采用 WinUI 3 / Windows App SDK。Material 3 是当前界面设计；不存在可单独删除的旧 WinUI 3 源项目。`windows/Hinge.App` 是当前应用源代码，必须保留。
- Windows 候选产物为 `publish\windows\Hinge-Setup.exe` 和 `publish\windows\Hinge-Windows.zip`；Android 主线包为 `publish\Hinge.apk`。安装器修复报告记录了构建、测试、哈希与 APK 签名核验信息。

## 已识别的旧安装与替换

- 旧安装目录：`D:\Hinge`，替换前 `Hinge.exe` 文件版本 `1.4.14.0`，873 个文件、约 348.6 MB。
- 用户明确选择替换 `D:\Hinge` 旧安装。替换前完整目录、桌面/开始菜单快捷方式及卸载注册项已保存至 `publish\rollback\pre-baseline-replace-d-hinge-20261011\`。
- 当前安装器以管理员权限运行成功（退出码 0），目标仍为 `D:\Hinge`；HKCU、ProgramData 安装路径以及桌面/开始菜单快捷方式均指向该目录。进程从 `D:\Hinge\Hinge.exe` 启动，因静默启动设置没有可见窗口句柄。
- 替换前后逐文件 SHA-256 对比：原有 873 个文件中 872 个与当前安装器完全相同；没有二进制变化，只移除了旧 `shell-integration-error.log`。新旧 `Hinge.exe`、`Hinge.dll` 和 Release 输出文件版本/哈希相同，仍是 `1.4.14.0`。因此此次是把安装状态规范回 `D:\Hinge` 并确认当前基线包一致，不是应用二进制升级。
- 本次没有删除 `windows/Hinge.App` 或其他 WinUI 3 源码，因为当前 M3 桌面客户端正由该 WinUI 3 项目实现；没有另一套独立的旧 WinUI 3 源项目可安全删除。

## 回退

完整回退快照位于：

`D:\App\Hinge\publish\rollback\pre-baseline-replace-d-hinge-20261011\`

快照包含替换前的 873 个安装文件、两个快捷方式和可用的卸载注册项备份。若要回退，仅恢复该目录对应的安装与快捷方式状态；不要回滚整个仓库或删除用户设置。项目源码及此前各项局部回退快照继续保留。

## 验收边界

本基线说明用于标识“当前主线 M3 版本”和“旧安装”的边界。它不把之前的视觉截图验证扩展解释为本次安装后的全主题、全 DPI 或真实手机传输验收；各页面覆盖范围以 [M3 验收报告](HINGE_M3_V4_ACCEPTANCE.md)、专项报告及其限制为准。

# 发布流程

本文是 Hinge 当前的可执行发布流程。发布前应先看 `docs/development.md` 的环境和验证说明。

## 操作前检查

任何修改、构建、签名、压缩、复制安装包或发布操作前，先完整审阅本文和 `docs/development.md`；完成后把实际版本、产物路径、校验结果和仍需验收的边界同步回文档。不要以旧的命令输出或旧发行版资产代替本次验证。

## 版本约定

- Android 应用版本来自 `android/pubspec.yaml`，当前历史开发版为 `1.0.32+33`；
- Android 和 Windows 的产品版本名保持为 `1.0.32`；本次历史开发版标签为 `v1.0.32-dev.1`。后续可交付版本直接使用稳定版本号（例如 `1.0.33`），GitHub Release 直接标记为 `Latest`，不再使用 `-dev`、`-pre` 后缀或 Pre-release 标签；
- Windows 发布只提供自包含 EXE 安装器和便携 ZIP，不提供 MSIX 或测试证书。
- EXE 安装器启用 .NET 单文件压缩；安装逻辑不变，发布前仍需验证安装、更新和目录选择。

## 发布前检查

```powershell
git status --short
flutter analyze --no-pub
flutter test --no-pub
dotnet build windows/Hinge.sln --configuration Release --no-restore
dotnet test windows/Hinge.sln --configuration Release --no-build --no-restore
git diff --check
```

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

Android 脚本会读取被 Git 忽略的 `android/android/key.properties`，优先使用其中的签名库路径、类型、别名和密码；未配置路径时回退到本机固定路径 `D:\Download\backup\infinitycm.bks`。脚本使用已验证的 `D:\jdk17` 构建，并将 BKS 临时转换为 PKCS12 供 `apksigner` 使用。别名、密码、签名库和临时副本不会进入仓库或日志；缺少签名输入时脚本直接失败，不生成可误装的调试签名更新包。CI 或临时构建仍可用进程环境变量覆盖本地配置。

Android 发布只使用 `android-arm64`，脚本在复制前检查 APK 只包含 `arm64-v8a` 并验证 APK 签名证书。唯一的 Android 本地发布产物是 `publish/Hinge.apk`；Flutter 的 `android/build/app/outputs/flutter-apk/app-release.apk` 仅为中间产物。

新 APK 复制成功后，脚本才会清理旧的 `publish/android/Hinge.apk`；签名失败或复制失败时不会删除旧包。

压缩由构建脚本负责：Windows 脚本负责单文件 EXE 压缩和便携 ZIP，Android/Flutter 负责 APK 的构建压缩与裁剪。APK 签名后禁止再次手工压缩或重打包，以免破坏签名；新增自动压缩步骤必须同时更新开发文档。

构建结束后检查：

- `publish/Hinge.apk` 存在且签名验证通过；
- `publish/windows/Hinge-Setup.exe` 可以选择安装目录；
- `publish/windows/Hinge-Windows.zip` 解压后可直接启动；
- 确认发行版资产只有 APK、EXE 和便携 ZIP，并分别记录 SHA-256。

## GitHub Release

1. 提交源码、协议和文档，不提交 `publish/` 二进制目录；
2. 后续稳定版本推送 `main` 和稳定版本标签（例如 `v1.0.33`）；
3. 创建 GitHub Release 并直接标记为 `Latest`；不再创建 Pre-release，除非明确发布需要保留的历史测试快照；
4. 上传 `publish/Hinge.apk`、`publish/windows/Hinge-Setup.exe` 和 `publish/windows/Hinge-Windows.zip`；
5. 使用 `CHANGELOG.md` 生成发布说明，并在发布页注明真机验收边界；
6. 发布后验证源代码提交、tag、Release 资产数量和文件 SHA-256。

## 安装器选择

EXE 是普通用户的推荐路径：它是自包含安装器，安装时选择目录，不依赖测试证书。便携 ZIP 适合不希望写入安装目录或需要直接解压运行的场景。

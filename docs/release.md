# 发布流程

本文是 Hinge 当前的可执行发布流程。发布前应先看 `docs/development.md` 的环境和验证说明。

## 版本约定

- Android 应用版本来自 `android/pubspec.yaml`，当前开发版为 `1.0.17+18`；
- Android 和 Windows 的产品版本名保持为 `1.0.17`；本次 GitHub 开发版标签为 `v1.0.17-dev.2`；
- Windows 发布只提供自包含 EXE 安装器和便携 ZIP，不提供 MSIX 或测试证书。

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
$env:HINGE_KEYSTORE_PASSWORD = '<keystore-password>'
$env:HINGE_KEY_PASSWORD = '<key-password>'
.\scripts\build_release_android.ps1
.\scripts\build_release_windows.ps1
```

构建结束后检查：

- `publish/android/Hinge.apk` 存在且签名验证通过；
- `publish/windows/Hinge-Setup.exe` 可以选择安装目录；
- `publish/windows/Hinge-Windows.zip` 解压后可直接启动；
- 确认发行版资产只有 APK、EXE 和便携 ZIP，并分别记录 SHA-256。

## GitHub Release

1. 提交源码、协议和文档，不提交 `publish/` 二进制目录；
2. 推送 `main` 和版本标签 `v1.0.17-dev.2`；
3. 创建标记为 pre-release 的 GitHub Release；
4. 上传 APK、EXE 和 ZIP；
5. 使用 `CHANGELOG.md` 生成发布说明，并在发布页注明真机验收边界；
6. 发布后验证源代码提交、tag、Release 资产数量和文件 SHA-256。

## 安装器选择

EXE 是普通用户的推荐路径：它是自包含安装器，安装时选择目录，不依赖测试证书。便携 ZIP 适合不希望写入安装目录或需要直接解压运行的场景。

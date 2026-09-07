# 发布流程

## 版本约定

- Android 应用版本来自 `android/pubspec.yaml`，当前为 `1.0.0+1`；
- GitHub Release 使用三段式产品版本标签，本次为 `v1.0.0`；
- Windows MSIX 使用四段式 AppX 版本，脚本会在本机已安装版本不低于源清单时递增修订号；
- Windows AppX 修订号与 GitHub Release 标签不是同一个字段，不应直接混用。

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
- 如果生成 MSIX，同时保留 `Hinge-Installer.cer`，并在干净环境验证安装提示。

## GitHub Release

1. 提交源码、协议和文档，不提交 `publish/` 二进制目录；
2. 推送 `main` 和版本标签 `v1.0.0`；
3. 创建非草稿、非预发布 Release；
4. 上传 APK、EXE、ZIP；若 MSIX 可用，再上传 MSIX 与 CER；
5. 使用 `RELEASE_NOTES.md` 和 `CHANGELOG.md` 生成发布说明，并在发布页注明真机验收边界；
6. 发布后验证源代码提交、tag、Release 资产数量和文件 SHA-256。

## 安装器选择

EXE 是普通用户的推荐路径：它是自包含安装器，安装时选择目录，不依赖测试证书。MSIX 保留给需要 Windows AppX 生命周期或已配置证书的环境；测试证书不应被当作正式商业签名证书。

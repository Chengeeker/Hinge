# 版本约定

Android 与 Windows 当前产品版本名统一为 **3.0.0**。

在用户正式发布 GitHub Release 前，后续修复和功能调整继续使用 3.0.0，不自动递增 minor/patch，也不改为 beta 后缀。源码推送、本地构建和安装候选不代表正式发布。

- Android：版本名和内部 `versionCode` 由 `android-compose/app/build.gradle.kts` 定义，当前为 `3.0.0+188`。正式构建入口为 `scripts/build_release_android.ps1`，签名产物为 `publish/Hinge.apk`。内部版本号继续单调递增。
- Windows：`windows/Hinge.Core/Constants.cs`、应用及安装器的产品版本一致；程序集/文件版本当前为 `3.0.0.0`。关于页和设备发现继续读取公共常量。
- Windows 稀疏包源码版本保持与产品版本校验一致；实际构建的集成包沿用独立递增身份版本，保留原包身份、签名和内容寻址的 Shell DLL 路径。

正式发布完成后，再恢复按功能与修复递增版本名；Android 内部构建号继续递增。版本调整需要同时核对运行界面、主 APK、Windows 安装器与便携 ZIP，不只修改源码文字。

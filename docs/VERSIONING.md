# 版本约定

当前发行版本为 Windows **3.0.2** 与 Android **3.0.2+198**。

双端版本名现统一为 3.0.2。Android 通过内部构建号区分同一版本的更新。

- Android：版本名和内部 `versionCode` 由 `app/build.gradle.kts` 定义，当前为 `3.0.2+198`。正式构建入口为 `scripts/build_release_android.ps1`，签名产物为 `publish/Hinge.apk`。内部版本号继续单调递增。
- Windows：`windows/Hinge.Core/Constants.cs`、应用及安装器的产品版本一致；程序集/文件版本当前为 `3.0.2.0`。关于页和设备发现继续读取公共常量。
- Windows 稀疏包源码版本保持与产品版本校验一致；实际构建的集成包沿用独立递增身份版本，保留原包身份、签名和内容寻址的 Shell DLL 路径。

新建发行版时，版本名按功能和修复递增，Android 内部构建号继续递增。版本调整需要同时核对运行界面、主 APK、Windows 安装器与便携 ZIP，不只修改源码文字。

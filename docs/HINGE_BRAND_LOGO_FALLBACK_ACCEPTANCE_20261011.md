# Hinge 未识别设备品牌默认 Logo 验收（2026-10-11）

## 修改范围

- 保留现有 Honor、vivo、Xiaomi/Redmi/POCO、Samsung/Galaxy、Huawei、OPPO 品牌识别与 SVG 资源。
- Windows 共用入口 `MainWindow.BuildOfficialBrandLogo`：识别到品牌时继续加载原 SVG；未识别时改用本地 `Assets/Brands/android-default.jpg`。首页设备卡片、附近设备、传输记录和文件管理机型标签继续复用此入口。
- Android `_brandAssetForValues`：上述品牌仍返回各自 SVG，其他品牌返回 `android-default.jpg`。设备摘要区域按扩展名分别使用 `SvgPicture.asset` 或 `Image.asset`，避免将 JPG 交给 SVG 解码器。
- Windows 项目将 JPG 作为内容复制到输出与发布目录；Android 沿用已有 `assets/brands/` 声明，无新增依赖。
- 默认图来自用户上传的 `D:\Download\images.jpg`，本地两份资源与源图 SHA-256 相同：`E54130FCA367A89A4EC120FE92C1AB86410AAD682E6EBC5C96D8572E9EAF1250`。不依赖运行时网络访问。

## 构建与包检查

- Windows Release 发布脚本成功；便携 ZIP 810 个文件全部可读取，包内 `Hinge.dll` 与 Release 输出一致（SHA-256 `307E0BEB3ECB1589F7025CD73784F886A27D873D43F5E856BEE610748F53DF1B`），并包含与上传图哈希一致的 `Assets/Brands/android-default.jpg`。
- Windows 安装器 `publish/windows/Hinge-Setup.exe`：203,907,731 bytes，SHA-256 `3256D41DA5974A1DA2A4DCE93578CDA9C4C7FF1D8A742BA79DE42AB1968EB973`。其 `HINGE_PAYLOAD_V1` 尾部 ZIP 长度与独立 ZIP 一致（135,523,914 bytes），嵌入 ZIP SHA-256 与独立文件一致（`C99A07D4ACBBD1B2EDA4B027529981026B2F5AA9044EAAFE38BF2DFB57A679BC`）。外层安装器未签名。
- Android Release APK：`com.hinge.office` 1.4.15+120，ARM64，21,163,270 bytes，SHA-256 `7976C4FD921A7A070DCDE81190B109EE7389BFF42D54216823FB964159B21001`。APK v2/v3 验签通过，证书 SHA-256 沿用 `3eb0f6708904f8ef916c6a2572e394ba981ee25d64861344cf1921ca7ea17975`；包内 `assets/flutter_assets/assets/brands/android-default.jpg` 为 5,075 bytes。
- Windows 和 Android 两份默认图片资源均与上传源文件哈希一致。

## 回退

变更前快照位于 `publish/rollback/pre-brand-logo-default-20261011/`，含修改前 Windows/Android 相关源码及安装器、便携 ZIP、APK。只回退本次触及的 `windows/Hinge.App/MainWindow.xaml.cs`、`windows/Hinge.App/Hinge.App.csproj`、`android/lib/app/app.dart`，并删除新增的两份 `android-default.jpg`；如需恢复交付包，再从快照对应的 `packages/` 目录恢复。不要用该快照覆盖整个当前工作树。

## 尚未验证

- 未安装候选包，也未在真实 Windows 窗口或 Android 设备中连接“无法识别品牌”的设备做 UI 截图验收。本次仅完成源代码构建、资源打包与签名检查；图片实际呈现位置和视觉观感仍需运行时确认。
- 用户上传图本身为 225×225 JPG，含 Android 字样及被裁切的绿色机器人上半部；按现有 Logo 容器等比适配，不进行图像重绘。

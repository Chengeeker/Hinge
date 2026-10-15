# Hinge v2.0.2 更新日志

本版修复手机和 Windows 显示已连接但文件浏览/短信命令不可用，以及 Android 自动重连反复提示的问题。跨端传输协议保持 `0.1`。

## 修复

- 并行的反向连接轮询与直连可能选中同一会话；直连任务随后将它当作重复连接关闭。两端现在保留最终获选会话（包括连接去重后替换的会话），只释放真正多余的连接。Android 对最终会话统一挂接数据接收，避免轮询选中的出站会话漏掉文件与短信数据订阅。
- Android 只复用仍然就绪的会话；握手后断开的连接不再登记为“已连接”，允许自动重试。自动恢复不再逐轮弹出开始、恢复、成功或失败提示；手动连接反馈保留。
- 多个 Android Activity/Flutter engine 交替期间，旧实例不再清除新实例持有的连接、短信和通知事件接收器；返回前台时恢复桥接并同步当前会话。

## 版本与发行资产

- Android：`2.0.3+124`，包名 `com.hinge.office`，仅 `arm64-v8a`。Android `2.0.2+123` 是未发布的本地候选，本次递增至 `2.0.3+124`。
- Windows：`2.0.2.0`。
- 发行资产：`Hinge.apk`、`Hinge-Setup.exe`、`Hinge-Windows.zip`。

## 验证结果与限制

- Android 定向 Flutter 测试 24/24 通过；所涉 Dart 文件静态分析通过。
- Windows 会话/配对测试 8/8 通过；从实际连接竞争分支提取并编译的自检通过直连获胜、同一 winner、释放多余连接、去重替换 winner 四种场景。
- Android APK 的 ARM64 ABI、正式 v2/v3 签名、证书指纹及 16 KiB zipalign 已核验。APK SHA-256：`66BCCE9F77C979F91003EEDB8E4ABE2E318AD0CA83DB8A858496B2C7303E392E`。
- Windows 便携 ZIP 共 810 项，内含 `Hinge.dll` 与当前构建输出一致，SHA-256：`CC41988143520CF263B92D29AE182011101A12A5F0C6847E6FEE60D6CD3AD06B`。Windows 安装器和 ZIP 已完成构建与版本核验。安装器外层 Authenticode 未签名。
- 用户现场确认文件列表刷新成功，并确认 Windows 收到了 Android“发送测试消息到 Windows”的测试提示；现场运行版本仍为 Android `2.0.2+123` / Windows `2.0.0.0`，所以这是会话恢复后的现场功能确认，不是新发行包安装后的长期验收。
- 新候选未覆盖安装到手机或替换正在运行的 Windows 客户端；锁屏/Activity 完全退出后的后台命令路由及持续连接尚未验收。

## 发行文件校验值

| 平台 | 文件 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| Android | `Hinge.apk` | 21,163,270 bytes | `66BCCE9F77C979F91003EEDB8E4ABE2E318AD0CA83DB8A858496B2C7303E392E` |
| Windows | `Hinge-Setup.exe` | 203,925,349 bytes | `70EBD37F22827C088CCADA7A8BD4D322551348ABA781733ABC170F66F6B548CA` |
| Windows | `Hinge-Windows.zip` | 135,541,545 bytes | `0B09F513495A11310DBA2C75AD23B975DEEE49314AD9148AAFC0E89C1EFD177C` |

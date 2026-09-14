# Hinge v1.1.6

这是 Hinge 的双端稳定版本，安装包运行版本为 Android `1.1.6+46`、Windows `1.1.6.0`。以下内容以 GitHub 上一版 `v1.0.38` 为基线，不重复列出此前已经发布的修复。

## 新增功能

- Windows 11 EXE 安装版新增资源管理器第一层“通过 Hinge 发送到”菜单：悬停后直接选择已连接机型，支持多选文件，并发送到 Android `/storage/emulated/0/Download/Hinge/` 下的图片、视频或文件分类目录；
- 便携版保留“显示更多选项”中的兼容菜单，不会为建立包身份而修改系统证书存储。

## 修复与改进

- 修复右键选择机型后只打开 Hinge、没有真正发送文件的问题；已运行的单实例现在通过当前用户专用命名管道接收目标设备和文件列表；
- 修复通知历史验证码字段在 Android 到 Windows 同步时丢失的问题；只有严格识别出的验证码显示复制按钮，普通数字通知不会误触发；
- 修复验证码托盘气泡点击后同时复制并打开 Hinge 窗口的问题；
- 修复 Android 通知历史数据库升级、异常厂商通知和短信观察器关闭竞态导致的闪退；
- 修复 Windows 任务栏灰色占位图标：安装器对齐稀疏包真实 AUMID，并提供符合 Windows 资源限定规则的完整小尺寸图标；
- 修复资源管理器兼容菜单落入文件关联弹窗、设备快照暂时不可读时菜单消失，以及更新后 Explorer 没有及时重新加载扩展的问题；
- Windows 设置新增“显示后台运行提示”开关，默认关闭。

## 安装与兼容性

- Windows 只提供 `Hinge-Setup.exe` 和 `Hinge-Windows.zip`；第一层原生菜单需要使用 EXE 安装版，安装器会在首次建立稀疏包身份时请求一次管理员确认；
- Android APK 仅包含 `arm64-v8a`，包名为 `com.hinge.office`；
- 安装更新后，历史信任设备仍会自动尝试恢复连接。

## 开源与合规

- Windows 11 原生菜单的 COM 激活框架改编自 Microsoft `vscode-explorer-command`（MIT），保留上游版权声明；设备枚举、快照、单实例传输和文件分类由 Hinge 实现；
- QQ/微信托盘唤醒仅参考 Electron（MIT）的公开消息约定，没有复制或打包 Electron 源码和运行时；
- 完整依赖和商标资源说明见仓库中的 `THIRD_PARTY_LICENSES.md`。

## 验证边界

- 自动化验证包括 Android 静态分析与 Flutter 测试、Android ARM64 Release 构建、Windows Release 构建与 .NET 测试；
- 资源管理器第一层菜单、真实手机短信权限、通知访问权限和不同局域网环境仍受 Windows/Android 系统策略影响，需要在对应设备上进行最终验收。

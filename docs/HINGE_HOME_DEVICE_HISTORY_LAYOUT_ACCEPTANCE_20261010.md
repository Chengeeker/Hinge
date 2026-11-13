# Hinge Windows 首页设备行与传输记录局部修复验收（2026-10-10）

## 修改范围

仅调整 `windows/Hinge.App/MainWindow.xaml.cs` 的附近设备行，以及 `windows/Hinge.App/HomePage.xaml.cs` 的传输记录行模板；没有改业务处理、主题、字体或页面导航。

- 设备行根因为三列虽已存在，但 42 DIP 图标列没有独立居中槽位、Logo 与内容没有显式间隔，状态/IP 文本未启用换行。现调整为 56 DIP Logo 槽位、`*` 详情列、`Auto` 操作列；详情距 Logo 12 DIP，状态/IP 可换行，行最小高度 72 DIP，操作列靠右。保留分隔线并使其从 Logo 槽位后开始。
- 传输记录短横线来自 `SetTransferHistory()` 在每行 Grid 中追加的 1 DIP `Border`。它没有指定列，默认绘制在第一列（Logo 列），因此正好出现在 Logo 下方。只删除该 Border；Logo、内容、删除/取消按钮和列表项状态均保留。

## 实际验证

- Windows Release 构建完成；ZIP 809 个条目全部读取成功，包内 `Hinge.dll` SHA-256 与 Release 输出一致：`7CA04FD1CEB56BF7EC46E32B743982B208ED7A24E7BF7D10AE6DDC86A52D23EF`。
- Windows 测试：180/180 通过；目标源码 `git diff --check` 通过。
- 在隔离 Release 候选实际启动首页。候选注入一个离线 UI 样例设备（之后为长 IP 检查改为在线状态），没有使用设备发现或连接其伪地址。宽窗口截图检查 Logo/名称/按钮分列及传输行下方无短线；窄窗口截图显示多 IP 地址换行且不进入连接按钮区域。页面滚动后，多条传输记录仍按行排列，Logo 下方没有横线。截图见本目录 `home-wide.png`、`home-narrow.png`、`home-history-scroll.png`。
- 运行时窗口 DPI 通过 `GetDpiForWindow` 实测为 120 DPI，即 125%。系统设置程序未向 Computer Use 暴露可操作窗口，因此 100%/150% 缩放未能实测；窄窗口截图是在 125% 下完成，不能代替这两档 DPI 验收。
- 未点击真实设备的连接/断开按钮，也未删除或清空用户记录，避免对真实设备会话和历史数据产生副作用。对应业务事件与既有同步/传输流程未改，Windows 180 项测试通过，但真实手机连接、传输及删除操作不是本次现场验收结论。
- 测试结束后只停止本轮隔离候选；用户原 Debug 实例未被替换或关闭。

## 交付与回退

- Windows 安装器：`publish/windows/Hinge-Setup.exe`，203,877,047 bytes，SHA-256 `FB9EDA47CF7DDD04B6FC0B2ED36A585180256BE10B95A70627C8514F484444C1`。
- 便携 ZIP：`publish/windows/Hinge-Windows.zip`，135,493,492 bytes，SHA-256 `947831893D8372A19B245D04E75833B2C1E5545F275B8BED42EE5E0486ECFAA4`。
- Android 源码未改，按项目规则重建主线 APK；`publish/Hinge.apk` SHA-256 仍为 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`，APK v2/v3 签名校验通过。
- 变更前源码和 Windows/APK 包快照位于 `publish/rollback/pre-home-device-history-layout-20261010/`。仅回退 `MainWindow.xaml.cs`、`HomePage.xaml.cs` 和对应交付包；不要重置整个工作树。
- 安装器 Authenticode 状态为 `NotSigned`，与此前本机候选包一致。没有安装候选、提交或推送。
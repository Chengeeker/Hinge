# Hinge Windows Material 3 V3 定向视觉修复验收报告

日期：2026-10-10  
范围：在现有 V2 WinUI 3 / M3 工作树上进行定向视觉修复；本报告是代码、构建和静态验收记录，不代表已完成图形界面现场验收。

## 输入与基线

完整阅读了桌面上的 V3 执行提示与技术文档，并查看了本任务提供的四张 Windows 当前界面图：

- C:\Users\Administrator\Desktop\Screenshot_20261010_004641.png（传输设置）
- C:\Users\Administrator\Desktop\Screenshot_20261010_085931.png（首页）
- C:\Users\Administrator\Desktop\Screenshot_20261010_085940.png（设置）
- C:\Users\Administrator\Desktop\Screenshot_20261010_090006.png（日历）
- C:\Users\Administrator\Desktop\20261007-174006-ff430f.jpg（用户提供的 Android M3 设置目标图）

提示词所说的任务包 before/ 与 reference/05_target_material_settings.jpg 在本机没有对应目录/文件；上面的截图是按用户提供的绝对路径直接查看的。Android 目标图作为设置页面的层级、表面和间距参考，不把它当作 Windows 像素尺寸规格。

迁移前工作树已包含 V2 M3 未提交更改。V3 开始前保存完整回退快照，未重置或覆盖该基线；工作树目前仍未提交。

## 本轮实现

- 设置页：[SettingsPage.xaml](../windows/Hinge.App/SettingsPage.xaml) 将内容列放宽并约束到 860 epx，按类别组织共享容器和容器内分隔行；标题、说明、图标与行高按 V3 尺寸建议收敛。子项继续复用现有导航和点击逻辑。
- 首页：[HomePage.xaml](../windows/Hinge.App/HomePage.xaml) 与 [HomePage.xaml.cs](../windows/Hinge.App/HomePage.xaml.cs) 将内容最大宽度约束到 1120 epx；窗口内容宽度达到 1050 epx 时显示双列摘要，不足时重排为单列。传输历史改为弱分隔行和清晰的动作层级。
- 文件管理：[FileManagementPage.xaml](../windows/Hinge.App/FileManagementPage.xaml) 与 [FileManagementPage.xaml.cs](../windows/Hinge.App/FileManagementPage.xaml.cs) 将分类项改为紧凑的透明列表（最小行高 44 epx）；未连接时显示说明和返回首页连接的入口。连接状态仍由主窗口刷新流程控制。
- 日历：[CalendarPage.xaml](../windows/Hinge.App/CalendarPage.xaml) 与 [CalendarPage.xaml.cs](../windows/Hinge.App/CalendarPage.xaml.cs) 降低网格线视觉权重，使用 16 epx 日期文字、38 epx 圆形选中标记和今日描边；工具栏增加可读标签，保留日期选择和日程操作路径。
- 传输设置内容列：[TransferSettingsPage.xaml](../windows/Hinge.App/TransferSettingsPage.xaml) 放宽并拉伸至最多 1000 epx，避免大窗口中的窄内容岛。
- 主题：在现有个性化页面中增加可选浅色 sage 样例，默认仍是 tonalSpot；偏好仅新增本地 WindowPalette 设置，深色和高对比度资源不被重写。没有新增第二套主题架构。
- 所有界面层变化沿用现有控件、导航、状态和资源。未改传输协议、远端存储结构、设备配对、Android 业务源码或 Windows 系统集成。唯一新增持久化值是本地浅色调色板选择。

## 验证结果

| 项目 | 结果 | 证据与边界 |
|---|---|---|
| WinUI Release 编译 | 通过 | dotnet build windows/Hinge.sln --configuration Release --no-restore -m:1 -nodeReuse:false；0 警告、0 错误 |
| Windows 回归测试 | 通过 | dotnet test windows/Hinge.Tests/Hinge.Tests.csproj --configuration Release --no-build --no-restore -m:1 -nodeReuse:false；180/180 |
| 静态资源/标记 | 通过 | 四个目标页面 XAML 可解析；40 个 sage 资源键均存在于 Light 字典且为 SolidColorBrush；CRLF-aware git diff --check 无空白错误 |
| Windows Release 包 | 通过 | 既有脚本原调用仍遇到“正在确定要还原的项目…”通用失败；直接发布成功，并用临时诊断副本确认完整包装成功。临时副本已删除，项目脚本未改 |
| Windows ZIP 完整性 | 通过 | 807 项均可读取；含 Hinge.exe、Hinge.dll、Hinge.Identity.msix，主程序哈希与发布输出一致 |
| Android ARM64 APK | 通过 | Flutter APK 中间产物已更新；既有签名脚本无交互地打出主线 APK；aapt 确认 com.hinge.office、1.4.14+119；APK v2/v3 验签通过，证书 SHA-256 未变 |
| 实际 WinUI 窗口、截图与操作 | 未验证 | 当前会话没有可用的 Windows 图形窗口控制/截图环境，winapp CLI 也不可用；没有安装或启动本轮候选，没有伪造 after 截图 |
| 真机连接与远端文件操作 | 未验证 | 未连接 Android 设备；传输、WebDAV、拖放、断连恢复等真实交互不由静态编译或单元测试代替 |

## V3 视觉门槛状态

| 页面 | 状态 | 尚缺验收 |
|---|---|---|
| S 设置 | UNVERIFIED | 运行候选后对照目标图检查实际宽度、分组表面、行高、文字层级、浅/深/高对比度及缩放 |
| H 首页 | UNVERIFIED | 检查 1050 epx 左右断点两侧、设备/历史长内容、空状态与按钮实际布局 |
| F 文件管理 | UNVERIFIED | 检查连接/断连状态、分类选择、拖放、缩略图及键盘焦点 |
| C 日历 | UNVERIFIED | 检查月份切换、今日/选中状态、日程标记、窗口缩放和深/浅主题 |

## 本地候选与回退

本轮 Windows 候选：

- publish/windows/Hinge-Setup.exe：1.4.14.0，198,261,168 bytes，SHA-256 6452FD2A4098E3618837383BC62D4E04E10CA673DBE7FE067DFE3EB23DF4DF1E。Authenticode 为 NotSigned；属于本地验收候选，不是可直接公开发布的签名安装器。
- publish/windows/Hinge-Windows.zip：129,877,613 bytes，SHA-256 F5A3021098A0EA62B1729315E05C22347BA78C82FE03829E9B3C12C0ADDFD237。
- publish/Hinge.apk：com.hinge.office 1.4.14+119，21,159,073 bytes，SHA-256 B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3。Android 源码未改，签名包哈希与 V3 前一致。

覆盖前快照位于 publish/rollback/pre-m3-v3-visual-repair-20261010/：

- Hinge-source-v2-working-tree.zip：SHA-256 3224BCEFA57D1DB01C32CDD0A73DDFC9D436B48F87DB25FE916649BE25F44418；含 V3 前 V2 工作树及 manifest。
- windows/Hinge-Setup.exe：SHA-256 12CA1E1313B82A7065EE6AB26BA237329591FDE012947A6E69A5B2F29EF64BAC。
- windows/Hinge-Windows.zip：SHA-256 DFE46DA2F1ECFFB2FFF3BD1A0EE5D2281FAF59C242329DF7BC055CD789039943。
- android/Hinge.apk：SHA-256 B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44C1A2E30B3。
- dev-docs/ 保存了 V3 前开发文档副本。

若视觉验收不合格，先从上述目录恢复对应 Windows/Android 候选包。源码快照应先解压到独立目录再按 manifest 对照/选择性恢复；不要直接覆盖工作树，以免一并丢失 V2 基线或其他本地更改。本轮未提交、未推送、未安装或发布。

## 结论

结构、间距、层级和组件状态已按 V3 建议落入代码，Windows Release 和既有回归测试通过；S/H/F/C 视觉验收仍全部未验证。完成验收需要在图形环境运行本地 Windows 候选并保存新截图，再按 V3 门槛复核。


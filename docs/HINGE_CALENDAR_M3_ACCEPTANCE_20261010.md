# Hinge Windows 日历页面 Material 3 专项改造验收报告

日期：2026-10-10  
项目锚点：`D:\App\Hinge`  
范围：`windows/Hinge.App/CalendarPage.xaml`、`CalendarPage.xaml.cs`；同步与协议层未改。

## 实现内容

- 月视图保留桌面 7 列布局，根据真实月份跨度创建 4、5、6 周，而不是固定 42 格；窗口尺寸改变时重新计算事件密度。
- 日期统一靠左上对齐；今天使用独立圆形强调，所选日使用柔和单元格表面，并保留单独的日期描边，因此今天与其他选中日状态可分别表达。非本月日期使用低对比度表面，网格线改为淡化的周分隔线。
- 使用项目现有主题画刷和 Typography 资源，支持系统浅色/深色语义画刷；日期按钮补全 Automation 名称、稳定 AutomationId、Hover/Pressed 与系统键盘焦点资源。
- 事件优先取 `RemoteCalendarEvent.EventColor` 并计算对比文字色；普通日程显示紧凑文字和颜色点，全天日程使用柔和填充标签，多日/跨周事件按周分段连续绘制。事件行数随格子高度调整，超量项以“+N 项”打开详情列表；详情可见日历名、类型与时间。
- 顶部保留上一月/下一月、今天、刷新、新建日程和日期跳转；新建、跳转取消路径未提交实际数据。
- 最终跨周事件横条下移到日期标记区域之后，并同步重算日程行空间，修正合成样例中发现的日期数字遮挡。

实现复用了现有 `RemoteCalendarEvent`、日期范围工具和页面主题资源；未修改 Android 日历同步、协议、事件创建/传输流程、系统集成或其他页面源码。调试期合成事件预览代码已从源码删除，Release 包不含合成数据分支。

## 实际验证

| 检查 | 结果与证据 |
|---|---|
| Release 编译 | `dotnet build windows/Hinge.sln --configuration Release -m:1 -nodeReuse:false` 成功，0 警告、0 错误。 |
| 动态月份周数 | 实际候选 UI Automation：2026 年 10 月 35 个日期按钮、2026 年 11 月 42 个、2027 年 2 月 28 个；分别为 5、6、4 周。 |
| 页面尺寸 | 在 125% DPI 下截图检查 1537×990、1280×800、1024×768；日期列仍为 7 列。 |
| 浅色/深色 | 两种主题下均检查今天与其他选中日期的独立状态；系统主题偏好已恢复为“跟随系统”。 |
| 非破坏性交互 | UI Automation 实际检查选择日期、“今天”、月导航、日期跳转对话框打开/取消、离线刷新提示、新建日程打开/取消；没有连接 Android 手机，也未创建远端日程。 |
| 事件视觉 | 真实手机未连接，故用一次性 Debug 合成样例检查来源颜色、全天与普通事件区别、跨周连续条、溢出入口及详情列表。修正后的截图为 `calendar-preview-events-offset-corrected.png`，屏幕注明“合成事件视觉预览 · 不含手机数据”。 |
| 最终发布视图 | `calendar-release-final-125-1537x990.png` 是 Release 输出在 125% 下的无手机日历截图；该视图没有事件，因此不涉及最后调整的事件横条偏移。横条偏移以随后 Debug 合成样例截图复核。 |
| 空白检查 | CRLF-aware `git diff --check` 通过；仅有 Git 对既存 CRLF/LF 工作树的转换提示。 |
| 功能测试套件 | 两次串行运行均为 166/180 通过，14 项失败；失败涉及 WebDAV 文件移动的 `UnauthorizedAccessException`、传输历史/待发送断言，以及两项传输等待超时，均不在日历代码路径内，原因未确认，本轮未修改这些无关功能。 |

截图目录：`D:\App\Hinge\tmp\calendar-ui-evidence\`。重点证据：

- 浅色：`calendar-125-1537x990-light-restored.png`
- 深色：`calendar-125-1537x990-dark-selected-top.png`
- 窗口尺寸：`calendar-final-125-1537x990.png`、`calendar-final-125-1280x800.png`、`calendar-final-125-1024x768.png`
- 4/6 周：`calendar-final-125-february-4-weeks.png`、`calendar-final-125-november-6-weeks.png`
- 事件样例（合成数据）：`calendar-preview-events-offset-corrected.png`

## Windows 候选与回退

- 使用临时发布脚本构建主线产物。原发布路径在本机遇到 SDK Workload Resolver locator 缺失、NuGet 漏洞源不可达，Shell 链接器报 `LNK1101 MSPDB140.DLL`，且 sandbox 阻止新建身份签名证书。未修改仓库发布脚本；通过串行发布参数解决还原问题，并从回退快照复用经哈希确认未变的同版本 Shell DLL 与身份 MSIX。
- `publish/windows/Hinge-Setup.exe`：198,272,909 bytes，SHA-256 `FD9EA9C4395635C426450C4768D0FD282A04F0437DCF5725291A5B76C13A1420`。
- `publish/windows/Hinge-Windows.zip`：129,889,354 bytes，SHA-256 `9A8E1243F44A5E85729905B2AE32A881B671633F6D66C8B30B2CF62FEC09207A`；807 项可完整读取。ZIP 中 `Hinge.dll` 与 `Hinge.exe` 均与最终 Release 发布输出哈希相符，安装器内附 ZIP 的长度与 SHA-256 均与独立 ZIP 一致。
- ZIP 中 `Hinge.Identity.msix` 与 `Hinge.ShellExtension` 和回退包逐字节相同；复用的 Shell DLL SHA-256 为 `F5A3B66A64EF6E194B7B198F55FA6D0F8EAC15844B62DE8C4C5977E89B46A258`。安装器 EXE 的 Authenticode 为 `NotSigned`，这是本地候选，不是带公开可信发布者签名的安装程序。
- 覆盖前回退快照：`D:\App\Hinge\publish\rollback\pre-calendar-m3-20261010\`，含日历 XAML/代码、Windows 安装器/ZIP、Android APK 和开发文档副本。可用 `source/windows/Hinge.App/CalendarPage.xaml` 与 `.xaml.cs` 恢复日历源码，或运行该快照中的旧安装器恢复原安装版本。当前候选未安装，未更换现有安装。

## 尚未验证/偏差

- 未测试 100%、150% 系统缩放、Windows 高对比度及真正键盘焦点/焦点切换；当前图形验证覆盖 125% 与浅/深色。
- 没有手机可连接，真实 `EventColor`、手机日历可见性、真实事件详情和创建/同步未在设备上验收；事件展示只由合成数据覆盖。
- 全量 Android Flutter Release 构建长时间没有进度，已停止；随后仅尝试签名/包校验也在脚本的密钥验证阶段停滞。通过独立 `keytool` 只读验证确认配置的 BKS 别名可用，但没有生成新 APK。`publish/Hinge.apk` 未覆盖，SHA-256 仍为 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`，与本次回退副本相同。所有本轮临时 PKCS#12 文件均已清除。
- 14 项非日历测试失败尚无根因结论；本报告不将它们记为通过，也不据此推断日历 UI 出错。

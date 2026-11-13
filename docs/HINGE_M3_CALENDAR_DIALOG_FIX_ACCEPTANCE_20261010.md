# Hinge Windows 日历选中态与 M3 弹窗专项验收报告（2026-10-10）

## 修改范围

本次只收敛日历日期选中态，并统一笔记、待办、日历中的 ContentDialog 样式与调用。没有重做日历网格、事件展示、同步协议或全局主题系统。

- `windows/Hinge.App/CalendarPage.xaml.cs`：移除选中日期铺满整格的背景与额外描边；日期数字继续使用 32×32 圆形指示器，今天使用主题主色，其他选中日期使用 M3 secondary-container，今天与选中重合时仍只显示一个圆形指示器。原有动态周数、跨天事件、事件颜色、溢出展开及选日逻辑未改。
- `windows/Hinge.App/MainWindow.xaml.cs`：复用原 ContentDialog `SemaphoreSlim`，使目标页面不再绕过单弹窗门闩；增加按需的 M3 对话框样式入口，使用现有 surface、Typography、按钮样式资源，并在弹窗打开后修正默认按钮状态可能重设的按钮样式。`DefaultButton` 和 Primary/Close 返回值语义保持不变。
- `windows/Hinge.App/NotesPage.xaml.cs`、`TodoPage.xaml.cs`、`CalendarPage.xaml.cs`：新建/编辑、删除确认、日历跳转、创建及消息弹窗都改用共享入口；复用现有 M3 文本框样式，并给标签、日期/时间输入应用 M3 资源。所有保存、取消、删除和手机日历调用路径保持原有实现。
- `windows/Hinge.App/Themes/HingeM3.xaml`：新增对话框标题/消息模板与 DatePicker/TimePicker 样式，继续使用现有 M3 语义资源。

## 构建与自动化检查

- `dotnet build windows/Hinge.App/Hinge.App.csproj -c Release -p:Platform=x64`：0 警告、0 错误。
- `dotnet test windows/Hinge.Tests/Hinge.Tests.csproj -c Release --no-restore -v diag`：180/180 通过。
- 目标源码 `git -c core.whitespace=cr-at-eol diff --check`：通过。
- Windows 安装包：直接调用发布脚本两次都在发布入口异常退出；使用相同脚本内容的临时诊断副本重跑后成功，且独立 `dotnet publish` 成功。ZIP 807 项均可读取；内含 `Hinge.exe`、`Hinge.dll` 与该次发布输出的 SHA-256 一致。未安装此安装包。
  - `publish/windows/Hinge-Setup.exe`：198,276,803 bytes，SHA-256 `E1F9246DEAF4BAE7DD84392C8DD2659F7908951127B13E05065D451C03181892`，FileVersion `1.4.14.0`。
  - `publish/windows/Hinge-Windows.zip`：129,893,248 bytes，SHA-256 `B19C332FC9720171CB470EF72A92BDB6621A4C650061D7941DFE99E7D34ECC2C`。
  - 安装器 Authenticode 为 `NotSigned`。
- Android ARM64 APK 按项目要求重建并重新签名；通过 `apksigner verify`，v2/v3 签名有效。包名 `com.hinge.office`、版本 `1.4.14+119`，SHA-256 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`，与构建前一致。Android 源码状态干净。

## 截图与交互验收边界

本轮没有取得修改后 UI 的可信截图，也没有实际点击日历日期或打开目标弹窗。WinApp CLI 安装卡在包安装阶段；Release 候选启动被 Hinge 单实例机制转发到当前已运行实例。为避免关闭或操作用户的现有实例，没有继续交互，因此浅/深色、不同 DPI、悬停/焦点状态、保存/取消及远端操作都未计为已验收。源代码保留了原来的业务调用与返回值检查，但这不能替代真实窗口回归。

本机临时预览开关和辅助启动代码已撤除；没有保留临时预览源码。当前仍有用户既有未提交工作树改动，本轮没有提交或推送。

## 回退

改动前工作树文件、开发文档、Windows 安装器/ZIP 和 Android APK 已快照至：

`D:\App\Hinge\publish\rollback\pre-calendar-dialog-fix-20261010\`

其中 `source\windows\Hinge.App\` 保存了目标源码基线，`packages\windows\` 保存构建前安装器、ZIP 和 APK。需要撤回本轮修改时，以该快照恢复相应文件/包；不要重置整个项目工作树，以免覆盖此前已有的未提交修改。

## 后续修复：新建日程与传输设置布局（2026-10-10）

- 用户反馈“新建日程”无响应。调用链仍从 CalendarPage.CreateButton_Click 构造并显示原有 ContentDialog，保存/取消及日历 Intent 业务没有改写。检查发现前次新增的共享 M3 弹窗 helper 在 Opened 事件中递归遍历 VisualTree 并重设内部按钮样式；这段操作存在与 WinUI 内置 ContentDialog 按钮生命周期冲突的风险，已移除。M3 主按钮、关闭按钮和标题/消息样式仍在调用 ShowAsync 前通过 ContentDialog 样式属性设置，公共弹窗门闩保留。未能在独立窗口现场重现，因此不将它表述为已实证的唯一根因。
- 用户提供的传输设置截图显示保存路径、配对码及右侧操作按钮横向溢出。TransferSettingsPage 缺少其他设置页已有的 ScrollViewer.ViewportWidth 到内容根宽度同步；现已补齐加载与尺寸变化时的同步，复用 Home/Settings 页面既有布局范式。
- Windows Release 构建：0 警告/0 错误；测试：180/180 通过；目标源码 diff --check 通过。标准发布脚本仍返回通用错误；同参数诊断副本的发布命令退出码 0，Windows Bundle 完整生成。ZIP 有 807 项，内含 Hinge.exe/Hinge.dll 哈希与本轮发布输出一致，安装器附加 ZIP 校验一致。
- 最终安装器 publish/windows/Hinge-Setup.exe：198,276,474 bytes，SHA-256 3ED817F9D8EFBB743BC47F415B10516ECB57D35AED5DC90ACD96DD0B24C44109，FileVersion 1.4.14.0，Authenticode NotSigned。ZIP publish/windows/Hinge-Windows.zip：129,892,919 bytes，SHA-256 FDBD021FFE3E505AC3B1756E8A268DBB2643F1860404B60BCF67B18B43BBFE4E。Android ARM64 APK 按项目要求重建并签名：publish/Hinge.apk 21,159,073 bytes，SHA-256 与构建前相同；APK v2/v3 验证成功，Android 源码未改。
- 没有取得修复后的真实窗口截图，也未在独立运行实例中点击新建日程。当前已有 Hinge Debug 实例未关闭或操纵；UI Automation CLI 在此环境不可用。因此本次只确认 XAML 编译、测试和包内载荷，不宣称完成运行时视觉验收。
- 回退快照：publish/rollback/pre-calendar-create-click-fix-20261010/，包含修改前的 MainWindow、传输设置页面文件、开发文档和旧包。需要撤回时只恢复这些目标文件/产物，避免重置含其他用户改动的工作树。
## 本次补充修复：ADB 高级诊断常显与日历新建（2026-10-10）

- `TransferSettingsPage.xaml`：移除 Android 无线 ADB 外层 `Expander`。高级诊断卡片现在常显标题、说明、简短状态及所有设置控件；原 host、pair/connect port、一次性 pairing code、clipboard sync、pair/connect/log buttons、详细状态 InfoBar 的 `x:Name` 与 AutomationId 未变，MainWindow 原事件绑定继续使用。ADB 协议、设置保存、日志导出、BLE 和传输业务均未改。
- `CalendarPage.xaml.cs`：`CreateButton.Click` 及表单字段、取消语义、时间转换和 `WorkspaceRemoteClient.OpenCalendarCreateAsync` 调用保留。把共享 M3 样式赋值拆成 `ApplyM3ContentDialogStyle`；创建表单绕过共享对话框 Semaphore，直接调用 WinUI `ContentDialog.ShowAsync()`，弹窗异常会显示在 `CalendarSummaryText`。这是为消除用户操作表单排队等待的路径调整；由于无法在独立图形实例中复现，点击修复效果尚未运行时确认。
- 检查：Release x64 `dotnet build` 0 warnings/0 errors；Windows 测试 180/180 通过；目标源码 CRLF-aware `git diff --check` 通过；ADB XAML 不再含 Expander 且核心控件名称仍在。Windows Bundle ZIP 807 项，包内 `Hinge.dll` 哈希与 Release 输出一致。
- Windows 包：`publish/windows/Hinge-Setup.exe` 198,275,156 bytes，FileVersion `1.4.14.0`，Authenticode `NotSigned`，SHA-256 `58EFE31102E30A7BFDA7FBE1B20D6950B012E570875881F43C997DD98EFC944E`；`publish/windows/Hinge-Windows.zip` 129,891,601 bytes，SHA-256 `96246C5F35BCEFE8EA5FF21D5FBFC3F4EC6D4A583F5551463351C6C11E6D27BF`。
- Android ARM64 APK 已按项目要求重建并签名；Android 源码未改。`publish/Hinge.apk` 为 `com.hinge.office` `1.4.14+119`，21,159,073 bytes，v2/v3 verify 成功，SHA-256 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`。
- 未进行截图或鼠标/键盘 UIA 点击验收：本轮没有操作用户现有 Hinge 实例，环境中 `winapp` CLI 不可用。浅/深色、窄宽度下常显区域布局，以及创建弹窗/Primary 按钮/真实 Android 日历跳转仍待用户安装此候选后实测。构建、包校验和自动化测试不代表这些 UI 场景已验收。
- 本轮变更前源码与当前 Windows/APK 包保存在 `publish/rollback/pre-adb-always-visible-calendar-create-20261010/`；详情见该目录内 `source/`、`docs/` 和 `packages/`。

## 新建日程再次修复：已复现根因（2026-10-10）

前两次推测没有解决用户的问题。本轮捕获真实错误：`Cannot find a resource with the given key: HingeM3TimePickerStyle`。其 BasedOn 引用了 SDK 中不存在的 DefaultTimePickerStyle；移除无效继承后，原生 TimePicker 默认模板正常加载。异常捕获已覆盖控件初始化，并恢复共享 M3 弹窗入口。

实际操作结果：新建弹窗成功打开、开始时间选择器打开、全天隐藏时间字段、空标题校验、校验后再次打开及取消通过。未提交有效事件，没有验证真机创建/同步。仅当前桌面浅色环境；深色与 DPI 全矩阵未验收。截图在 tmp/calendar-timepicker-evidence，详见开发文档第 312 节及正式安装包哈希。GUI 候选只临时使用独立 AppInstance 键，正式源码恢复、交付包正常身份已核验。原有用户 Debug 实例保留，安装新版后需要彻底退出旧进程再运行。

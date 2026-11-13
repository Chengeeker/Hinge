# Hinge 首页传输记录自适应高度与文件宫格列数验收（2026-10-10）

## 改动与根因

- `windows/Hinge.App/HomePage.xaml` 原将 `TransferHistoryListView.MaxHeight` 固定为 240 DIP，导致窗口最大化后记录列表仍保持旧高度。
- `windows/Hinge.App/HomePage.xaml.cs` 在首页加载、窗口视口变化、顶部设备卡片布局变化、传输记录变化后，读取滚动视口、内容根和列表的实际高度，将列表最大高度设为扣除其余首页内容后的剩余视口空间；剩余不足时保留 120 DIP 的列表并让页面继续滚动。项目项、取消/删除按钮及历史滚动逻辑不变。
- `windows/Hinge.App/FileManagementPage.xaml` 移除 `ItemsWrapGrid.MaximumRowsOrColumns="5"`，让 GridView 使用默认无限行/列上限，依容器边界按项目宽度自然换行。Windows App SDK 文档说明默认值 `-1` 表示不限制，详见 [ItemsWrapGrid.MaximumRowsOrColumns](https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemswrapgrid.maximumrowsorcolumns)。
- 普通文件卡片 148×144 DIP 等既有尺寸不变。分页、虚拟化、缩略图和文件操作流程未改。

## 验证结果

- 首页高度公式的数值自检通过：示例视口 752 DIP 从初始 240 调整为 340 DIP，重新布局后数值稳定；短视口下保持 120 DIP 最小高度；视口变高时列表上限随之增长。
- Windows Tests：180/180 通过。
- Windows Release 构建成功，MSBuild 0 warnings / 0 errors。便携 ZIP 809 项完整读取无损坏，含 6 个品牌 SVG；ZIP 内 `Hinge.dll` SHA-256 与 Release 输出一致：`798DCDD1E4B9B412EA2533D6917FD46A44B67FF8AF1F2CD77D2A568AC44AA127`。
- APK 按项目规程重建（Android 源码未改），v2/v3 签名验证通过，SHA-256：`B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`。
- Windows 安装器：`publish/windows/Hinge-Setup.exe`，203,877,569 bytes，SHA-256 `F334BECCA9D4C74A74A0FA04FAB066BC7C0CDCCCF06E2822A34D0B2BC921AC8C`。
- Windows 便携包：`publish/windows/Hinge-Windows.zip`，135,494,014 bytes，SHA-256 `7BB353A8AD8F71A41A38622024B3CE094307F145EC603E49EF44F8019E081BFC`。
- 图形自动化未枚举到 Hinge 窗口；运行进程 MainWindowHandle 为 0。故本轮没有改后最大化/缩放界面截图，也没有现场测量宫格列数和列表底边。代码、构建及计算自检不能替代窗口实际验收。安装器 Authenticode 为 `NotSigned`。

## 回退

`publish/rollback/pre-home-history-height-file-grid-columns-20261010/` 保留本轮改动前的 `HomePage.xaml`、`HomePage.xaml.cs`、`FileManagementPage.xaml`、开发文档、Windows 安装器/ZIP 与 APK。回退时只还原这些目标文件和需要恢复的包，不要重置整个有未提交改动的工作树。

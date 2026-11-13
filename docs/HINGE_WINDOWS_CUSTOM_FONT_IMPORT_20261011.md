# Windows 个性化自定义字体导入验收（2026-10-11）

## 实现

- 个性化页新增字体选择、样例预览和“导入字体文件或 ZIP”入口。内置 HarmonyOS Sans SC 保留为可恢复选项。
- 支持 TTF/OTF 单字体文件，以及包含 TTF/OTF 的 ZIP。导入时读取 OpenType `name` 表获取字体系列名；仅将验证通过的字体复制到当前用户的 Hinge 本地数据目录 `HingeCustomFonts`，不会安装到 Windows 系统字体目录。
- ZIP 内容使用应用生成的随机文件名保存，不按归档内路径落盘；单字体最多 30 MiB、每批最多 32 款且最多 120 MiB。设置保存选中字体文件名，重启后从本地目录恢复。
- 通过 `ContentControlThemeFontFamily` 应用字体，沿用项目现有 HarmonyOS Sans 全局 Typography 资源与 M3 页面样式。

## 构建与静态验证

- `dotnet build windows/Hinge.App/Hinge.App.csproj -c Release -p:UseSharedCompilation=false`：成功，0 警告、0 错误。
- Windows 正式 Release 包构建成功；便携 ZIP 含 810 项，`Hinge.dll` 与 Release 输出 SHA-256 一致：`521A53B6B3236C4A65B29F08F2DADA831E49D2842E7E2C6E21F946C9EDF53CA7`。
- Windows ZIP：135,533,073 bytes，SHA-256 `3900D56CA7072AB551B165CDB5492346D125C446364016C46F30191BDC661510`。
- Windows 安装器：203,916,890 bytes，SHA-256 `B39B07E9A80DED209F783CB15EF69016633BB5E83F20B9778C21B84A538A61B8`；尾部 `HINGE_PAYLOAD_V1` 标记及载荷长度有效，内嵌 ZIP SHA-256 与独立 ZIP 一致。Authenticode 为 `NotSigned`。
- 按项目规程重建 Android 主线 APK；Android 源码未改。`com.hinge.office` `1.4.15+120`，21,163,270 bytes，SHA-256 `7976C4FD921A7A070DCDE81190B109EE7389BFF42D54216823FB964159B21001`；v2/v3 签名验证通过。
- 没有安装/运行 Windows 候选版，没有通过 UI 导入字体、重启恢复、浅深色渲染或长文本/图标覆盖检查；这些运行时视觉与交互仍待验收。未运行测试。

## 回退

改前三个目标源码文件及 Windows 安装器、Windows ZIP、Android APK 位于 `publish/rollback/pre-custom-font-import-20261011/`。`CustomFontLibrary.cs` 是本轮新增文件，源码回退时应删除。其余工作树文件不属于本次修改范围。详细步骤与哈希见快照内 `ROLLBACK.md`。

## 点击无响应问题的后续修复（2026-10-11）

- 将“导入字体文件或 ZIP”的 `Click` 从 `MainWindow.ContentFrame_Navigated` 中的动态事件订阅改为 `PersonalizationPage.xaml` 的页面内直接绑定。这样不依赖页面导航后的绑定时序。页面会显示正在打开、取消选择、无效文件、成功及失败状态；文件选择器和导入异常会转成可见错误文字。
- 发布版本与安装目录 DLL 内容哈希一度不同。原因是安装器增量提取只比较文件长度和压缩包/目标时间戳，未比较内容哈希；修改前后 DLL 长度相同，构建产物时间戳不足以触发覆盖。最终 Windows 包将 `Hinge.dll` 的源时间戳置于安装版之后，重新安装后 `D:\Hinge\Hinge.dll` 与发布 DLL SHA-256 一致：`E9FA2FEA0497EBF0429BDDC78D6003FA271C9AC2D3207E4F8A05436EBCDD753C`。
- 最终 Windows Release 构建成功，0 警告/0 错误。正式安装器 SHA-256：`E632E19C4C177FFF6F63434462538AE715F1231AF29CED0C28C7E4DC28894D3B`；便携 ZIP SHA-256：`1F2B096DEE4ABB070839B0FE390744633084289A58E883492EE622AB509935B0`。Android 源码未变；主线 APK 重新签名校验，SHA-256 `7976C4FD921A7A070DCDE81190B109EE7389BFF42D54216823FB964159B21001`。
- 最终安装器已更新 `D:\Hinge`，安装目录 DLL 与该构建一致。安装后从 `D:\Hinge\Hinge.exe` 启动，进程响应正常，生命周期日志到达 `main-window-activated`。
- 未完成按钮实点、文件选择器打开截图及导入字体后的渲染验收：当前桌面自动化工具能枚举浏览器，但没有可用的原生应用绑定接口；虽可确认窗口句柄与启动状态，不能据此推断点击已通过。请用户下次打开“个性化”实点确认；若仍失败，页面会直接显示错误信息供定位。
- 回退目录：`D:\App\Hinge\publish\rollback\pre-font-import-click-fix-20261011\`，含修复前 3 个相关源码文件及原 Windows 安装器、ZIP、APK。源码回退只恢复其中 `PersonalizationPage.xaml`、`PersonalizationPage.xaml.cs`、`MainWindow.xaml.cs`，不要恢复全工作树。


# Windows HarmonyOS Sans ZIP 导入异常修复（2026-10-11）

## 根因与修改

用户导入此前提供的 `HarmonyOS-Sans.zip` 时，个性化页显示 `The type initializer for 'Hinge.App.CustomFontLibrary' threw an exception.`。崩溃日志确认异常来自 `CustomFontLibrary` 静态字段初始化：免 MSIX 包身份运行的 WinUI 3 程序访问 `ApplicationData.Current.LocalFolder` 时抛 `InvalidOperationException`，因此字体 ZIP 解析甚至尚未开始。

- 字体目录改为 `AppContext.BaseDirectory\HingeCustomFonts`，避免依赖包身份。
- 字体族引用改为应用目录资源 URI `ms-appx:///HingeCustomFonts/...`，与新目录位置一致。
- 每批字体上限从 32 提高到 64；用户 ZIP 自检出 48 个真实 TTF/OTF，合计 79,025,216 字节，落在原 120 MiB 批次上限内。归档 `__MACOSX` 中的元数据副本不计入真实字体。
- TTF/OTF 验证、随机文件名保存、30 MiB 单字体限制、120 MiB 批次限制和字体选择持久化逻辑不变。

只修改 `windows/Hinge.App/CustomFontLibrary.cs`。没有修改传输、主题、Android 字体或协议逻辑。

## 验证

- `dotnet build windows/Hinge.App/Hinge.App.csproj -c Release -p:UseSharedCompilation=false`：成功，0 警告、0 错误。
- Win-x64 self-contained Release 发布成功；包脚本因既有 app publish 子步骤异常，使用同脚本其余阶段并复用刚验证的发布输出完成 Windows bundle。Shell Extension 0 警告、0 错误；包 ZIP 中 `Hinge.dll` SHA-256 与发布输出一致：`957EF8D254B65DAEE82C632D82024F58F3717525CF4EF8F9353BA74B27439E98`。
- 安装到既有 `D:\Hinge` 后，安装目录 DLL 与发布 DLL SHA-256 一致。启动 `D:\Hinge\Hinge.exe` 后，进程 `Responding=True` 且有主窗口句柄，生命周期记录 `main-window-activated`；启动后的 crash.log 时间戳未变化，没有新增初始化异常。
- HarmonyOS ZIP 边界自检：48 款真实字体，79,025,216 bytes；符合代码的 64 款与 120 MiB 上限。
- Android 源码未变，按项目维护要求重建主线 APK，签名构建脚本成功。APK SHA-256：`7976C4FD921A7A070DCDE81190B109EE7389BFF42D54216823FB964159B21001`。
- Windows Release 编译、包内 DLL 校验和安装后启动均完成。由于当前图形自动化没有可用的原生窗口交互，尚未实际点击个性化页面导入该 ZIP，也未验证字体切换、重启保留及字体实际渲染；这些验收仍需在 GUI 中完成。

## 交付包

- 安装器：`publish/windows/Hinge-Setup.exe`，203,917,521 bytes，SHA-256 `F964AB60574A113F48BF8EAEA22AC9075DB1008BF32BAE0C7D3EC31BE884DF8D`。
- 便携 ZIP：`publish/windows/Hinge-Windows.zip`，135,533,704 bytes，SHA-256 `DEB870134D8C28B6B9B6C323258CE92E79C6353E1B360A3422BCF72B035ADDDA`。
- Android APK：`publish/Hinge.apk`，21,163,270 bytes，SHA-256 如上。

## 回退

本修复前源码快照为 `publish/rollback/pre-custom-font-appdata-fix-20261011/CustomFontLibrary.cs.before`。还原该文件即可回退代码；此前正式安装包位于 `publish/rollback/pre-font-import-click-fix-20261011/`。如需回退安装版本，先退出 Hinge，再运行该快照中的 `Hinge-Setup.exe` 并指定 `D:\Hinge`。新导入字体会写入 `D:\Hinge\HingeCustomFonts`；若执行源码回退而保留用户字体，可单独保留此目录。


# 字体系列 / 字重选择、重复导入与删除（2026-10-11）

## 修改

- 字体解析读取 OpenType `name` 表族名（16/1）、字重/样式（17/2）、版本（5）和 PostScript 名称（6）。个性化设置拆为“字体系列”和“字重 / 样式”两个选择框。该 HarmonyOS Sans 包经名称表检查为 6 个字体系列、48 种不同的系列/样式组合；例如 `HarmonyOS Sans SC · Thin/Light/Regular/Medium/Bold/Black`，不再堆叠为同名项。同系列同字重但来源不同的字体会附带源文件名和版本信息。
- 导入文件按 SHA-256 去重，存储文件名也采用内容哈希，重复 ZIP 不再累积相同字体。导入不会擅自切换当前应用字体；若导入新样式，用户在系列与字重列表中自行选择。
- 新增“删除所选字体”按钮和 M3 确认框。删除前临时恢复内置字体；相同字节内容的重复副本一并清理。内置字体不可删除。
- 字体数据在列表载入时计算 SHA-256；系列/字重切换使用已载入目录缓存，避免每次操作都重新读取整包字体。

## 验证及交付

- Windows Release `dotnet build` 成功，0 警告、0 错误；发布安装器及便携 ZIP 成功。
- 名称解析边界自检：HarmonyOS Sans ZIP 中 48 个有效 TTF 对应 6 个系列、48 个唯一系列/样式组合。
- 重复包自检：本机字体库中 48 个文件与原 ZIP 逐一按 SHA-256 匹配；新逻辑对该 ZIP 判定 48 个重复、0 个新增。
- 已安装至 `D:\Hinge`；安装后的 DLL 与 Release 输出及 ZIP 内 DLL SHA-256 一致：`66FA4D1EFFF03CC0CCD4851F36D4FAAE7038BDDE63E6A2D638DA169771AB920D`。启动窗口进程 `Responding=True`，有主窗口句柄。
- 按项目维护要求重建 Android 主线 APK；源码未改，构建脚本成功。
- 原生窗口自动化当前不可用（CUA 仅列出浏览器，没有原生应用），故没有实点删除、切换字重、导入重复包或截图验证实际字体渲染；构建和哈希自检不能替代 GUI 验收。

## 回退

改前安装器、便携 ZIP、APK 和已安装 DLL 快照见 `publish/rollback/pre-font-weight-dedupe-delete-20261011/`。要回退安装版，退出 Hinge 后运行 `Hinge-Setup.before.exe` 并指定 `D:\Hinge`。现有字体数据目录 `D:\Hinge\HingeCustomFonts` 独立保留；回退时不要删除它。

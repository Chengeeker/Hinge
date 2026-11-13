# Windows 全局字体切换刷新修复验收（2026-10-11）

## 根因

个性化页选择字体时会更新 `Application.Resources["ContentControlThemeFontFamily"]`，预览控件也会单独设置 `FontFamily`。但应用在运行中替换资源字典项，不会自动让当前页面里已创建的 `TextBlock`、输入控件和按钮重新解析主题资源，因此表现为预览可能变化，正文仍像内置字体。

## 修改

- 只修改 `windows/Hinge.App/MainWindow.xaml.cs`。
- 继续维护 `ContentControlThemeFontFamily` 作为全局字体入口。
- 字体切换时递归刷新主窗口视觉树中已实例化的 `TextBlock`、`RichTextBlock` 和 `Control`，并在 Dispatcher 下一轮再刷新一次，覆盖刚创建的模板元素。
- 页面导航完成配置后同样刷新，确保页面切换后新显示的页面使用当前字体。字体图标控件没有被当作普通文本控件覆盖。
- 字体解析、系列/字重选择、预览、导入删除、持久化、主题资源定义和 Android 字体均未改。

## 验证

- Windows Release 构建：成功，0 警告、0 错误。
- Windows Release `dotnet publish`：成功。
- Windows 打包脚本的直接 WinUI publish 阶段失败；复用刚成功的同配置 Release publish 输出，执行其余既有打包步骤成功。便携包中的 `Hinge.dll` 与 Release 输出 SHA-256 均为 `98288E4D96A7424075434E37DF5D802B182DD2734A26E51E23F240E510E38F44`。
- 安装器：`publish/windows/Hinge-Setup.exe`，SHA-256 `31ACB9AC05EF80276EDD8B99E3A04F3AED4741AEFC7A4B3B75AE4F3C3E3D4DDE`。
- 便携 ZIP：`publish/windows/Hinge-Windows.zip`，SHA-256 `A8BFAF6857C28EDB1349A622836FD60A41622B3663664C3C0C58843CF38D61E5`。
- Android 主线 APK 已依项目规程重建，源码无变化；SHA-256 `7976C4FD921A7A070DCDE81190B109EE7389BFF42D54216823FB964159B21001`。
- 安装到 `D:\Hinge` 后，安装 DLL 与发布输出 SHA-256 同为 `98288E4D96A7424075434E37DF5D802B182DD2734A26E51E23F240E510E38F44`；本地 48 个用户字体文件保留。启动后 Hinge 进程 `Responding=True`、未退出。
- 这轮工具环境没有可用的原生窗口交互能力，未在 GUI 中切换字体、拍摄前后截图或检查多页面混排，也不能据此宣称实际渲染验收通过。编译、安装和包内哈希仅证明改动进入运行产物。

## 回退

改动前 `MainWindow.xaml.cs`、旧安装器、旧便携 ZIP、旧 APK 及旧安装 DLL 均备份在 `publish/rollback/pre-global-font-refresh-20261011/`。源码回退仅恢复此目录中的 `MainWindow.xaml.cs.before`，不要 reset 整个工作树。回退安装版时退出 Hinge，再运行 `Hinge-Setup.before.exe` 并指定 `D:\Hinge`。用户的 `D:\Hinge\HingeCustomFonts` 数据目录不属于本次改动，不要删除。

## Side-navigation icon regression follow-up (2026-10-11)

The initial global font refresh set `FontFamily` on every `Control`, including navigation and icon-hosting containers. That allowed private-use icon glyphs to inherit a text font lacking those symbols. Narrowed the visual-tree refresh to `TextBlock`, `RichTextBlock`, `TextBox`, `PasswordBox`, and `RichEditBox`; generic `Control` containers are no longer overridden. The custom font still applies to visible text and text-entry controls, while `SymbolIcon`/`FontIcon` keep their icon font.

- Release build: 0 warnings/errors; Windows bundle built and installed at `D:\Hinge`.
- Installed DLL SHA-256 matches Release output: `22D62F9396FC01D7BE2AC1C70E92EC9E8B13F8E5CCDF848136A28B3B2EAAF3F7`. Hinge starts with `Responding=True`.
- Captured the running window after install at `tmp/font-icon-scope-fix-window.png`; the sidebar home, files, notes, todo, calendar, photos, notifications, settings and about icons are visibly rendered. This confirms the reported sidebar icon regression is corrected in the installed candidate.
- Setup SHA-256: `E66854F2DA711E799B055EBAA180F7F2FAA5734D28DB037704B1282CFF10D5C2`; portable ZIP SHA-256: `7730A39920692BA4F389299B00A8D815E6F2ECF08F8EE50CFAB9323ACD7CB45E`; Android APK rebuilt, SHA-256 `7976C4FD921A7A070DCDE81190B109EE7389BFF42D54216823FB964159B21001`.
- Before-fix source and artifacts are in `publish/rollback/pre-font-icon-scope-fix-20261011/`.

## Sidebar icon revalidation after user reports page-switch disappearance (2026-10-11)

The earlier home-only screenshot was insufficient. The remaining risk was the full-tree `TextBlock` walk: WinUI icon templates can expose private-use glyphs through internal text elements. The refresh now returns immediately for `IconElement` subtrees and does not replace `FontFamily` on any `TextBlock` containing Unicode private-use glyphs. Generic navigation/container controls remain untouched.

- Installed and launched the newest build; installed `Hinge.dll` matches Release/ZIP payload SHA-256 `77F9AAF7295920DD9B663E6DB9E12CA2EB04A30F3C233DADBCAC471C821541FD`; process responded normally.
- Used UI Automation to select imported `HarmonyOS Sans SC（导入）` + `Regular`, then navigated Home, Files, Notes, Todo, Calendar, Photos, Notifications, and Settings. Captured each page; the navigation rail icons remained visible throughout. Contact sheet: `tmp/nav-customfont-sidebar-contact-sheet.png`.
- Restored the original built-in font selection and returned to Home after the check.
- Latest setup SHA-256 `C6CD6FA3B905673F6FB39AC0C6DEA49CFA1D6D774B187E488A331327466953C1`; portable ZIP SHA-256 `330A036C26D331DD9A2044F9C4E4486FF9670FE379F367BCFA3BA1678A2E7BCF`; Android APK SHA-256 remains `7976C4FD921A7A070DCDE81190B109EE7389BFF42D54216823FB964159B21001`.
- This verifies that this installed build retains sidebar icons across all listed pages while an imported font is selected. If the user's current window still shows missing icons, it may be an older process/build or a different icon; compare its installed `D:\Hinge\Hinge.dll` hash against the hash above before further changes.

# Hinge Windows clickable-area cursor fix (2026-10-11)

## Change

WinUI 3 does not expose `UIElement.ProtectedCursor` publicly. Added small derived controls that set the native hand cursor only on enabled clickable targets, and clear it when disabled. Existing button styles and click handlers remain in use.

Coverage includes XAML and code-created buttons, navigation items, clickable ListView/GridView items, File Management command-bar buttons, and the WebDAV select/cancel buttons. Text, input fields, combo boxes, and the sidebar resize handle retain their existing pointer behavior. HyperlinkButton keeps its built-in link cursor. No dependency, business logic, transfer protocol, or global theme changes were added for this fix.

## Verification

- WinUI Release build: succeeded, 0 warnings / 0 errors.
- Windows tests: 198/198 passed.
- Windows Release installer and portable ZIP built at version `1.4.14.0`.
- ZIP: all 809 entries read successfully; packaged `Hinge.dll` SHA-256 matches Release output: `108BA1E01D882D343B956E5C23238BDBA0E1357358A9A1085E59527B6389C4E8`.
- Installer `publish/windows/Hinge-Setup.exe`: 203,902,598 bytes, SHA-256 `77A4D0BE8E88C9DF45F45D2725FC298B895B7223517293A3719730FA85446F09`; outer Authenticode status is `NotSigned`.
- Portable ZIP `publish/windows/Hinge-Windows.zip`: 135,518,781 bytes, SHA-256 `717669F674854C4F15280ACC831C6529C0560E15A4FB317428B15D2F5B7A3D5C`.
- Android sources were unchanged. Mainline APK rebuilt as `com.hinge.office` `1.4.14+119`; v2/v3 signature verification passed. `publish/Hinge.apk`: 21,159,073 bytes, SHA-256 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`.
- Previous installer, ZIP, and APK were copied before replacement to `publish/rollback/pre-pointer-cursor-20261011/packages/`.

## Not verified

No Hinge window was available for a real pointer-hover screenshot or mouse acceptance, so this report does not claim that the cursor appearance was visually confirmed at runtime. The candidate was built but not installed over `D:\Hinge`; hover behavior should be confirmed after launching the candidate.


## Follow-up: consistent hand pointer on Windows interactive controls (2026-10-30)

Windows 2.7.4 / Android 2.7.4+142 broadens the existing shared hand-cursor behavior to interactive WinUI controls. The native link-select hand is applied to the hit-tested visual subtree of focusable controls and selector items, realized list items, open ComboBox options, and ContentDialog action buttons. Text-entry controls (TextBox, PasswordBox, RichEditBox, NumberBox, AutoSuggestBox) keep their normal text cursor. This is control-scoped; it does not replace the pointer globally. Sidebar items, single-select capsules, buttons, and create/cancel dialog actions were checked. The existing native ComboBox types remain in place to avoid repeating the PhotosPage template crash fixed in 2.7.3.

An offline WinUI probe loaded PhotosPage, FileManagementPage, and PersonalizationPage in light and dark themes, checked the hand cursor through hit-tested descendants, exercised enable/disable and selection states, and checked dialog Save/Cancel buttons while retaining the text-entry cursor. All assertions passed (`INTERACTIVE-HAND-CURSOR-OK`, `DIALOG-ACTIONS-HAND-CURSOR-OK`). This is a UI-property probe, not a physical mouse-hover or installed-app acceptance.

The Windows Release installer and portable ZIP and the mainline Android ARM64 APK were rebuilt. APK package/version/ABI are `com.hinge.office`, `2.7.4` / `142`, `arm64-v8a`; v2/v3 signing and 16 KiB alignment passed, the signing certificate matches the pre-change rollback APK, and all 294 ZIP entries were read successfully. Windows 2.7.4.0 and Android 2.7.4+142 artifacts were not installed; no phone/ADB session or GitHub push was used. Previous packages and the edited source/version files are retained in `publish/rollback/pre-interactive-hand-cursor-20261030/`.

- `Hinge-Setup.exe`: 229874608 bytes, SHA-256 `F401330CB5B25F059E08EF6A0D2B32FA172D978FB6FE7FAA92E86BD1B071DC81`
- `Hinge-Windows.zip`: 161488373 bytes, SHA-256 `C2E94D9E8C67E28C7D366D877AEAA053D01A3259F7622D09D0B77C25F202936D`
- `Hinge.apk`: 22290327 bytes, SHA-256 `E8E527DDA3705FE032339CD2EDD558DDFBEB9253BC25D578B4BD341CF4E21DDF`
## Follow-up: single-select capsule hand cursor (2026-10-30)

- 用户复测指出个性化页的单选胶囊仍出现忙碌光标。保留共享交互光标逻辑，并新增 `HandCursorRadioButton`：在派生的 WinUI `RadioButton` 自身直接设置 `ProtectedCursor`，禁用时清除；个性化页三个主题选项均使用该控件。输入框、选项逻辑和主题状态未改。
- Windows 主线版本 `2.7.5.0` 完整发布包构建成功。安装器 `229875708` bytes，SHA-256 `E2462E91A272EE2CDD9E9C3AF97B1A537AE6772D29093D7087319CBB586C8ADE`；便携 ZIP `161489475` bytes / 838 项，SHA-256 `3939528CEA65B7E9654B78F5C6B9F31ECB51C9D9802A2F8E7916444D49747007`。包内 `Hinge.dll` 与最终发布目录哈希一致。标准发布脚本第一次以低日志级别执行时只返回还原失败、没有错误详情；临时使用详细日志重跑完整发布流程成功，未改发布脚本。
- 修改前安装器、ZIP、APK 保存在 `publish/rollback/pre-interactive-cursor-real-hover-20261030/packages/`。用户明确将自行安装和验收，因此本轮不再用 Computer Use 移动鼠标或安装候选；实际手形悬停结果仍由用户验证。未连接/操作手机或 ADB，未推送 GitHub。
- Android ARM64 APK 本轮未产出：Hinge 构建在 Flutter 启动脚本的全局 `flutter.bat.lock` 处等待，期间另一项 Review 构建仍在使用共享 Flutter 工具链。为不干扰该构建，已停止本轮等待；`publish/Hinge.apk` 仍是旧版 `2.7.4+142`，不能当作本次候选安装包。用户本次要求自行验证 Windows 安装包，故仅交付 Windows 安装器/便携包；若后续需要 Android APK，再在 Flutter 锁释放后单独构建并验签。

## Follow-up: apply hand cursor to RadioButton template hit targets (2026-10-30)

The user tested 2.7.5 and still saw the loading / vertical-resize cursor on the choice capsule. `HandCursorRadioButton` set `ProtectedCursor` only on its outer control. WinUI hit testing lands on the template's text/icon child, and #436 had omitted the shared `HandCursorResource.AttachNative(this)` call that applies the cursor to that hit-test subtree. The constructor now attaches the shared handler; the three personalization theme options continue using this derived control.

The Windows Release build succeeded as 2.7.6.0. A source assertion confirms the derived control attaches the subtree handler and all three options use it. The portable ZIP has 838 readable entries; packaged `Hinge.dll` matches the final publish output, and the setup payload matches the portable ZIP. The Android mainline ARM64 APK was also rebuilt as 2.7.6+144; package `com.hinge.office`, v2/v3 signatures, the existing release certificate, arm64-v8a-only ABI, and 16 KiB alignment were verified. Neither package was installed; the user will check real mouse hover behavior. No Computer Use, phone/ADB operation, or GitHub push was performed.

- `Hinge-Setup.exe`: 229875649 bytes, SHA-256 `A348AED7C64705C9F9E066D36F03DDAB7AD608D2C63CA43E9F786A39492AC762`
- `Hinge-Windows.zip`: 161489417 bytes, SHA-256 `35C964A8006EB3AB05646BA6733E5DDF76381FAC8E4B79C685619EB313C64B85`
- `Hinge.apk`: 22290327 bytes, SHA-256 `0D08605A4EAD1E3FF9EE383BC36F9D3D8E34D5D7C300885E1A7CEA5A0B34A1BC`

Rollback: the user reported that 2.7.6 looked worse and asked to revert. Restored the pre-2.7.6 source and exact 2.7.5 Windows installer/ZIP plus the pre-change Android APK from `publish/rollback/pre-radio-template-cursor-fix-20261030/`. Current Windows packages are 2.7.5.0; the cursor issue remains unresolved. Do not treat the 2.7.6 build notes above as the current candidate.

## Follow-up: restore the default cursor only for single-select capsules (2026-10-30)

Per the user's clarification, the three personalization theme choices use the built-in WinUI `RadioButton` again, so they retain the Windows default cursor. The capsule-only subclass is removed. The shared hand-cursor filter excludes both `RadioButton` and its `RadioButtons` group; this is required because the shared visual-tree cursor handler would otherwise apply a hand cursor through the group. Other shared hand-cursor targets (buttons, navigation and other interactive controls) are unchanged.

Windows 2.7.7.0 Release installer and portable ZIP were built. Source assertions confirmed the native radio controls and that other custom buttons still call the shared hand-cursor handler. ZIP contains 838 entries and the packaged `Hinge.dll` matches the final publish output; the setup payload matches the portable ZIP. The mainline ARM64 APK was rebuilt as 2.7.7+145 (`com.hinge.office`), with v2/v3 signing, the existing certificate, arm64-v8a-only ABI, and 16 KiB alignment verified. No candidate was installed or tested with Computer Use; the user will verify the native cursor. No phone/ADB operation or GitHub push was performed.

- `Hinge-Setup.exe`: 229875317 bytes, SHA-256 `B67590C15A31043072BF5B62855904AA8AE756B47A264A04AFE5F10A14CC5D84`
- `Hinge-Windows.zip`: 161489090 bytes, SHA-256 `1EBE4DF3F3162F3A43CCAF220A3343C36873462A7BD0F730E3BC209F515D9A2C`
- `Hinge.apk`: 22290327 bytes, SHA-256 `DBCD84A9962E7D19BD010E4541BD50C57073E15761F54E5CC448C2EFCB90FDEE`

## Follow-up: prevent the theme capsule from blocking the UI thread (2026-10-30)

The user reported that clicking a single-select theme capsule still showed the spinning cursor. The built-in WinUI `RadioButton` and `RadioButtons` remain excluded from the custom hand-cursor path, preserving their Windows default cursor. Tracing the selection callback found that `ApplyWindowTheme` synchronously cleared and rebuilt the hidden Home device list on every theme change. Device list changes are already refreshed by `DeviceRegistry.DevicesChanged`; the theme-triggered rebuild was redundant and could stall the UI during the click. Removed only that rebuild; theme application and all other cursor targets remain unchanged.

Windows 2.7.8.0 Release installer and portable ZIP rebuilt. Portable ZIP has 838 entries; its packaged `Hinge.dll` SHA-256 matches the publish output (`85A4313F0FFE8974A6026A06986C04C1C9D5B959CAEF0900463C6B9D57DC98A1`), assembly version `2.7.8.0`; installer file version is `2.7.8.0`. ARM64 mainline APK rebuilt as `2.7.8+146`, package `com.hinge.office`, ABI `arm64-v8a` only, v2/v3 signatures and 16 KiB alignment verified; signer matches the stable release certificate. No installation, Computer Use, phone/ADB operation, or GitHub push was performed; the user will verify the actual pointer behavior.

- `Hinge-Setup.exe`: 229875323 bytes, SHA-256 `3E332793C0910533772A615B028C004E0F159CCA6196395246C1E557D69B8826`
- `Hinge-Windows.zip`: 161489093 bytes, SHA-256 `9B1673586FAAB3E68D3C6735660BD26861FC5EAE8ADEBF1BD5E3D80F5A19F624`
- `Hinge.apk`: 22290327 bytes, SHA-256 `3F90CD33E9696995B4CC5DA4048FC3A6AF54AC6E4275045201FDD7E4A247C763`

Rollback: pre-change 2.7.7 packages and the previous source/version files are in `publish/rollback/pre-radio-theme-refresh-cursor-fix-20261030/`. The standard Windows script initially returned a generic publish failure; rerunning its same pipeline with normal MSBuild verbosity completed successfully. Flutter's batch launcher waited on the SDK lock, so the cached Flutter tool was invoked directly after confirming no active Dart/Flutter build process; the standard release signing/validation script then signed and checked the APK.

## Follow-up: apply the window theme through one inherited root (2026-10-30)

The user confirmed that 2.7.8 still showed the spinning cursor on click; the prior hidden-device-list change was insufficient. `ApplyWindowTheme` also set the same `RequestedTheme` separately on `PageRoot`, `AppNavigation`, and `ContentFrame`, although the latter two are descendants of `PageRoot`. This made one theme selection update multiple visual subtrees in sequence. The app now changes only `PageRoot.RequestedTheme`; navigation and frame inherit the effective theme. Native radio controls remain uncustomized, and title-bar theming/home status styling remain intact.

Windows 2.7.9.0 Release installer and portable ZIP rebuilt. The 838-entry ZIP's `Hinge.dll` matches the publish output, assembly and installer versions are `2.7.9.0`, and the Setup-embedded ZIP hash/length match the portable ZIP. Android mainline APK is `2.7.9+147`, `com.hinge.office`, arm64-v8a only; v2/v3 signature, release certificate, and 16 KiB alignment verified. No installation, real mouse check, phone/ADB operation, or GitHub push was performed; the user will verify the cursor behavior.

- `Hinge-Setup.exe`: 229875260 bytes, SHA-256 `DA93CE0158B0DF0013C9733955E07A0F0EB2FC9E648B660BC129442567AB943F`
- `Hinge-Windows.zip`: 161489028 bytes, SHA-256 `EDCA18679F629D1DDE2024DCA87DA164890F5D6ED03319FB914C6D45F03C5352`
- `Hinge.apk`: 22290327 bytes, SHA-256 `371E91B4AC885CC23C95E7BD5F6E9A2B58A9106169E751EA8A9A7FE7CBDB435C`

Rollback: pre-change 2.7.8 artifacts and sources are in `publish/rollback/pre-root-only-theme-apply-cursor-fix-20261030/`. Android was built with the cached Flutter tool after the batch launcher waited on the SDK lock; release signing and validation used the project's normal script.

## Follow-up: default cursor for all single-selection capsules (2026-11-03)

The user confirms 2.7.9 still spins on click and that the issue affects selection capsules throughout the app. The previous theme-only optimizations were insufficient. The shared cursor helper still treated ComboBox/ComboBoxItem as hand targets and explicitly decorated popup options on DropDownOpened. RadioButton/RadioButtons exclusions also did not stop inherited hand cursors from an interactive ancestor.

The helper now clears custom cursor properties on all four native selection control types and their template children, stops recursive hand propagation at that boundary, and guards previously attached PointerEntered handlers against selection-control ancestors. Removed the popup-option hand override. Existing button/navigation/list-item hand behavior and native selection/M3 templates remain. This is a confirmed coverage correction; the actual spinning symptom remains pending user mouse acceptance. No Computer Use, installation, phone/ADB operation, or GitHub push is authorized by this task.

Candidate versions: Windows 2.7.10.0; mainline Android ARM64 2.7.10+148 (version synchronization only). Previous 2.7.9 packages/source files: publish/rollback/pre-native-selector-cursor-fix-20261030. Build/package results follow below.
- 最终构建与静态检查：Windows 2.7.10.0 Release安装器/便携包成功；ZIP 838项CRC读取通过，Hinge.dll与最终发布输出一致，Setup内嵌ZIP长度/哈希与独立ZIP一致。Android 2.7.10+148，包名com.hinge.office，仅arm64-v8a；v2/v3签名、既有正式证书和16KiB对齐通过。报告tmp/cursor-2.7.10-artifacts.json。未安装或实际鼠标测试，未操作手机/ADB，未推送GitHub。
- Hinge-Setup.exe: 229875186 bytes，SHA-256 D1911DC16F1D1F72B69891E5D5FAE202CF193EFF27ACEC0C96C9CF2BDDEDA43D
- Hinge-Windows.zip: 161488943 bytes，SHA-256 F83F5A8D52C911A7430FC987C5FB66289FBF6B8A4E86D8598E98597C85B6B09C
- Hinge.apk: 22290327 bytes，SHA-256 1E3C9D9EF5439F6DDB2799FAF938C2AB916B12D33B981E8C4D0BB2BBB9CD6D84

## Follow-up: native popup class cursor diagnosis (2026-11-03)

2.7.10 did not fix the user's click-without-movement symptom. Read-only inspection of the installed 2.7.10 process found two `Microsoft.UI.Content.PopupWindowSiteBridge` HWNDs with `GCLP_HCURSOR == 0`, whereas the main window class had a nonzero cursor. No production window/control was operated or modified.

[WinUI issue #8829](https://github.com/microsoft/microsoft-ui-xaml/issues/8829) reports the exact wait/resize cursor until mouse movement behavior, including reproduction in the WinUI Gallery. This explains why excluding XAML hand-cursor targets and reducing theme work were insufficient. The native popup fallback, rather than the theme callback, is the new diagnosed target.

`WinUiPopupCursor` uses the documented [SetClassLongPtrW API](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setclasslongptrw) to supply the current system arrow only to a missing popup class cursor, scoped to the calling UI thread and exact WinUI popup class name. It preserves nonempty class cursors and does not move the pointer, poll it, or change Windows mouse settings. After correcting a missing class cursor it refreshes the current arrow once via SetCursor only if WindowFromPoint matches this UI thread and the exact popup class. A hidden, unactivated WinUI startup probe found no popup HWND immediately after Window construction, so the shared ComboBox open event is essential for lazy first creation; the guarded current-cursor refresh prevents waiting for a physical mouse move. Startup initialization also handles any existing popup windows. No new library or derived ComboBox is introduced.

The focused hidden-window regression test passes (1/1): caller-thread scope, exact class targeting, default-arrow assignment, preservation of unrelated/nonempty class cursors, and repeated-call behavior. Log: `tmp/popup-class-cursor-tests.log`. This checks the native class property; it is not real pointer acceptance. Previous artifacts/source are retained under `publish/rollback/pre-popup-class-cursor-fix-20261103/`. Candidate: Windows 2.7.11.0 and Android arm64 2.7.11+149. Build/package results follow below.
- 最终构建/静态包检查：Windows Release 2.7.11.0成功，ZIP 838项CRC完整，包内Hinge.dll与最终发布输出一致（SHA-256 
D32721952B5D92228DAE7F280C9651DC53C6950FA367B2BEEF42973966CD2614
），Setup内嵌ZIP与独立ZIP长度及哈希一致。最终生产源码后的定向原生回归1/1通过。Android 2.7.11+149正式arm64主线APK成功，com.hinge.office、仅arm64-v8a、v2/v3签名及既有正式证书、16KiB对齐检查通过；Android仅版本同步。日志tmp/popup-class-cursor-windows-final-build.log、tmp/popup-class-cursor-android-build.log，报告tmp/popup-class-cursor-artifacts.json。
- Hinge-Setup.exe: 229876110 bytes，SHA-256 50E91989EBB366C61F07457A2B79CB7F9F0D7BAC22EC13881E7146C794C3F631
- Hinge-Windows.zip: 161489868 bytes，SHA-256 9A25648F3353B79A2C19072D5640090161D2B4011658E70A17954BC74DFE271C
- Hinge.apk: 22290327 bytes，SHA-256 D11EBDA2C42CA709033C04D03AEFEC3CE7B1675FA30825388331BE8E1B48AD98
- 保留范围：未使用Computer Use、未实际点击/移动鼠标、未安装或修改运行中的Hinge、未操作手机/ADB、未推送GitHub；实际点击显示效果待用户安装新版验收。

## Follow-up: explicitly enforce the system arrow for selectors (2026-11-03)

The user reports that 2.7.11 can still show a vertical-resize cursor even while the phone is connected, and explicitly requests an arrow on all selection capsules. They also associate some busy-cursor cases with disconnected devices; this change does not diagnose or repair that connection/UI-work issue.

ComboBox, ComboBoxItem, RadioButton and RadioButtons now receive an explicit system Arrow cursor throughout their template hit targets, rather than a null/default inherited cursor. Loaded/enable changes and drop-down close reapply the policy. Every drop-down open handles realized items immediately and once more on the UI queue while still open. The native popup helper enforces Arrow even over a stale nonempty class cursor, and refreshes the stationary pointer on each opening rather than only when the class was first corrected. Thread/class checks remain; no pointer movement, polling, or Windows mouse-settings changes. Text inputs and other controls' existing hand behavior remain.

Native hidden-window regression: 1/1 passes, including stale vertical-resize correction and thread/other-class scope. A separate unactivated WinUI check links the production cursor sources and verifies 23 Arrow hit targets plus disabled/selection-change states, ordinary button Hand, and untouched text-input cursor. Its ThemeBrushes resource refresh is stubbed, so it does not verify actual theming or real pointer/drop-down behavior. No Computer Use, installation, phone/ADB operation, or production-process mutation was performed.

Candidate: Windows 2.7.12.0 and Android arm64 2.7.12+150 (version synchronization only). Rollback: publish/rollback/pre-selector-arrow-cursor-20261103. Logs: tmp/selector-arrow-native-tests.log, tmp/selector-arrow-probe-build.log, tmp/selector-arrow-windows-build.log, tmp/selector-arrow-android-build.log. Package results follow below.
- 最终构建与包检查：Windows 2.7.12.0 Release安装器/便携包、Android 2.7.12+150主线arm64包均成功。ZIP 838项CRC通过，Hinge.dll与最终发布输出一致，Setup内嵌ZIP与独立ZIP的长度/哈希一致。APK为com.hinge.office、仅arm64-v8a，正式证书、v2/v3签名、16KiB对齐及CRC检查通过。报告tmp/selector-arrow-artifacts.json；未安装候选、未实际点击或使用Computer Use、未操作手机/ADB、未推送GitHub。
- Hinge-Setup.exe: 229876638 bytes，SHA-256 ED435F3DDDFCA237AE9BAA83F273A0F60EC6353837FC80A5C737402C4491A1F6
- Hinge-Windows.zip: 161490393 bytes，SHA-256 EAD0BE336FB56B850425AA23B4656679FA7A17E31544972600EE36CB2B4AA7DF
- Hinge.apk: 22290327 bytes，SHA-256 6FF7BC3967BFD07D63BCAFEE9376E8BAF3F84752A17C826452EC7339B2D0EBEF

## Follow-up: native popup visibility and input-child handling (2026-11-03)

The user confirms 2.7.12 still shows the spinning cursor until physical movement. Read-only inspection verified both the installed EXE and loaded Hinge.dll are 2.7.12, with the DLL matching the distributed hash. Four hidden native popup HWNDs have a null class cursor, and each has an InputSiteWindowClass child. The main content bridge also has an input child. This does not prove which HWND the real pointer hit during the reported click, but establishes that checking the exact WindowFromPoint class cannot cover the real native hierarchy.

The helper now follows GetAncestor(GA_ROOT), not the owner chain, and observes EVENT_OBJECT_SHOW scoped to this process and UI thread. Window-level native visibility notifications refresh the popup cursor after the native show path, independently of the existing XAML DropDownOpened/deferred callbacks. Registration occurs after MainWindow creation; real Window.Closed unhooks it. A static delegate roots the callback, exceptions are contained at the native boundary, and only the first three matching native shows are logged. Startup logs monitor registration success in the existing lifecycle log. No timers, pointer movement, cross-process event monitoring, injected DLLs, or security-policy changes are introduced.

The focused native test passes (1/1). It creates hidden Popup and InputSite child windows, checks ancestor/thread/class scope, delivers native show notifications through NotifyWinEvent and pumps the message queue, verifies automatic/repeated cursor correction, and verifies cleanup prevents further correction. The windows are never shown and the production app/mouse are not operated. This tests notification wiring and native properties; real ComboBox pointer behavior remains user acceptance.

Candidate: Windows 2.7.13.0, Android arm64 2.7.13+151 (version only). Rollback: publish/rollback/pre-native-popup-show-cursor-20261103. Logs: tmp/native-popup-show-tests.log, tmp/native-popup-show-windows-build.log, tmp/native-popup-show-android-build.log. Package results follow below.
Final input-path addition: selection controls handle Mouse PointerPressed/PointerReleased with handledEventsToo enabled, restoring the system arrow during the actual input event even if WinUI already handled it. The handler requires current coordinates inside the selector and a physical hit HWND on the same UI thread, so captured releases outside the selector/app do not override another cursor. Realized ComboBoxItem containers attach the same handler. Only the first three input corrections are logged. This branch compiled against the restored SDK; it was not exercised with a real mouse. Final Windows build log: tmp/native-popup-show-windows-final-build.log.
- 最终交付检查：最终源码后原生事件回归1/1通过；Windows 2.7.13.0 Release安装器和838项便携ZIP构建成功，包内Hinge.dll与最终输出哈希一致，Setup内嵌ZIP长度/哈希与独立ZIP一致。Android 2.7.13+151仅版本同步的主线arm64包成功，com.hinge.office、仅arm64-v8a、既有正式证书、v2/v3签名、16KiB对齐和CRC通过。报告tmp/native-popup-show-artifacts.json，APK检查tmp/native-popup-show-apk-check.txt。
- Hinge-Setup.exe: 229878523 bytes，SHA-256 E596F7AD6955C954D5C422AA1DFFC9973B3E1796BB2EC57C6C45B20DEAA56E8F
- Hinge-Windows.zip: 161492279 bytes，SHA-256 C1252A840DF1D7D2E5F5E797C8BA8D70BEB8F9A46A34EEDDA183869D8C5FC0ED
- Hinge.apk: 22290327 bytes，SHA-256 7DFD66634643E665207D0013BA5ACAE3225EBA938EE6550144B88FCC2E181DCD
- 验收边界：只读检查了当前生产进程元数据/窗口结构；回归使用隐藏窗口的原生事件通知，没有真实ShowWindow或鼠标点击。实际指针按下/抬起路径完成编译，未进行真实鼠标操作。未安装候选、未使用Computer Use、未操作手机/ADB、未推送GitHub。前述确定性代码缺口已修正，实机点击是否彻底消除转圈仍待用户验收。

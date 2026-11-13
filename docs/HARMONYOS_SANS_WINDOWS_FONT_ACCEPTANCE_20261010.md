# HarmonyOS Sans SC Windows 字体接入验收

日期：2026-10-10。按当前 Hinge Windows 工作树完成；不是 OS 安装字体。

## 变更

- `windows/Hinge.App/Assets/Fonts/HarmonyOS_Sans_SC_Regular.ttf`：字库原文件，8,261,128 bytes。Family 名称经 Windows PrivateFontCollection 实测为 HarmonyOS Sans SC。
- `windows/Hinge.App/Assets/Fonts/LICENSE-HarmonyOS-Sans.txt`：保留许可证正文及 Copyright 2021 Huawei Device Co., Ltd.。
- `windows/Hinge.App/App.xaml`：将 WinUI 全局 ContentControl 字体资源指定为内嵌 Harmony 字体。
- `windows/Hinge.App/Themes/HingeM3.xaml`：六类 M3 TextBlock Typography 显式使用该字体。
- `windows/Hinge.App/Hinge.App.csproj`：字体和许可证复制进构建/发布输出。
- `windows/Hinge.App/AboutPage.xaml`：产品信息中显示字体及版权署名。

HarmonyOS Sans License Agreement 允许将未修改字体嵌入/捆绑/随应用再分发，要求应用显著声明字体使用并在副本中保留版权和协议。本次按要求加入关于页面署名和 LICENSE 文件。系统 SymbolThemeFontFamily 未改，所以应用 glyph 图标不被文字字体替代。

## 实际 UI 检查

在保持用户原运行进程不被替换的隔离 Windows Release 实例中打开首页、日历、个性化窗口材质 ComboBox 和关于页。四页截图显示中文/拉丁混排由新字库渲染；列表菜单仍可正常弹出；日期、导航、Symbol 图标均正常；About UIA text 确认字体署名可见。截图目录：`docs/visual-acceptance/20261010-harmony-font/`。

| 页面 | 截图 |
| --- | --- |
| 首页 | home-0.jpg |
| 日历 | calendar-0.jpg |
| 个性化与窗口材质菜单 | personalization-0.jpg、material-menu-0.jpg |
| 关于页及许可署名 | about-0.jpg |

本次可视环境为 Windows 125% 当前 DPI、浅色鼠尾草绿主题；未完整重测 100%/150%、暗色/所有 palette、所有 Unicode/Emoji。Emoji、字体文件未覆盖字形将按 WinUI/Windows 回退。项目只打包简体中文 Regular 字重；现有 FontWeight 使用 Windows 对 Regular 字体作合成，不附带额外 Medium/Bold/Italic SC 文件。

## 构建与验证

- Windows `scripts/build_release_windows.ps1` 成功；portable ZIP 完整性检查通过，共 809 项；字库大小/hash 与源包解出版本一致；版权与 license 文本存在；EXE installer 按既有格式内嵌同一 ZIP。
- Windows 安装器：[publish/windows/Hinge-Setup.exe](../publish/windows/Hinge-Setup.exe)，203,877,006 bytes，SHA-256 `894F54CD0108C8FD69D93D52EC4018C1BA5900B59C898C33F60EFE799CD45EDE`，Authenticode NotSigned。
- Portable：[publish/windows/Hinge-Windows.zip](../publish/windows/Hinge-Windows.zip)，135,493,451 bytes，SHA-256 `2680091237F4EED519881BDAC455BCB8FC351CC26EA58112586A143CDB8C956A`。
- 项目要求的 Android ARM64 Release APK 也重建成功（Android 代码和资产未变），v2/v3 签名验证通过；21,159,073 bytes，SHA-256 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`。

## 回退

`publish/rollback/pre-harmonyos-sans-font-20261010/` 保存接入前的四份源码目标、开发文档和 Windows/APK 交付件。回退时恢复 `App.xaml`、`Themes/HingeM3.xaml`、`Hinge.App.csproj`、`AboutPage.xaml`，并恢复先前 Windows 包；仅选择本次目标文件，保留其他现有修改。

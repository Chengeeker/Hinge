# Hinge v2.2.0 更新日志

本版对照 GitHub 上一正式发行版 [`v2.0.2`](https://github.com/Chengeeker/Hinge/releases/tag/v2.0.2)，汇总其后进入当前发行版的新增功能与界面更新。新增功能按最终提供的能力说明，不拆分记录开发过程中的后续调整。

## 新增功能

- Windows 新增“手机投屏与远控”独立页面，可通过已配对的 Android 无线调试连接启动投屏或触控控制。安装器和便携版内置所需组件，无需另外下载 scrcpy。
- 投屏支持 H.264、H.265、AV1，自定义帧率上限、码率、最长边分辨率，以及硬件优先或软件解码；页面显示实时渲染帧率和跳帧数。帧率设置为采集上限，实际输出取决于屏幕内容和设备能力。
- Android 新增独立的投屏与远控入口，提供无线调试配对、使用和撤销授权说明。
- Android 新增电脑向手机发送文件时的系统通知进度；传输过程中显示进度，完成后更新为完成通知。此能力适用于 Hinge 主连接上的普通文件传输。
- Android 个性化设置新增液态玻璃效果开关，可选择将悬浮底栏显示为液态玻璃样式；默认关闭，且只影响悬浮底栏。

## 界面更新

- Windows 按 Material 3 视觉层级更新桌面表面色、卡片圆角、页面留白和导航样式；收起侧栏时图标居中，侧栏文字保持适中字重。
- Android 底栏尺寸、位置和选中样式对齐 Review 的悬浮底栏设计；液态玻璃效果可在个性化设置中单独启用。

## 版本与发行资产

- Android：`2.2.0+126`，包名 `com.hinge.office`，仅 `arm64-v8a`。
- Windows：`2.2.0.0`。
- 跨端传输协议：`0.1`。
- 发行资产：`Hinge.apk`、`Hinge-Setup.exe`、`Hinge-Windows.zip`。

## 验证状态与限制

- Android ARM64 Release APK 已构建，并核对版本、ABI、签名与 16 KiB 对齐；SHA-256：`43582CDC1479850F7AD24CE41740AD9A00FB79C09E6FFEB33422F5E3218BAF10`。
- Windows Release 安装器和便携 ZIP 已按 `2.2.0.0` 构建；相关 Windows 回归 `251/251`、Flutter 页面回归 `15/15` 通过。
- Android 传输进度通知和液态玻璃底栏的最终 UI/长时间使用效果尚未在设备上验收。投屏帧率取决于采集内容与设备能力，帧率数字不代表均匀的屏幕呈现节奏。
- Windows 安装器外层 Authenticode 未签名。

## 发行文件校验值

| 平台 | 文件 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| Android | `Hinge.apk` | 21,749,655 bytes | `43582CDC1479850F7AD24CE41740AD9A00FB79C09E6FFEB33422F5E3218BAF10` |
| Windows | `Hinge-Setup.exe` | 229,315,293 bytes | `27546A018054C0F7AABA59827E85688EC5F804851F2A619F35C6FF22816214D3` |
| Windows | `Hinge-Windows.zip` | 160,931,488 bytes | `CF2857C484FAFD45E2CE89019921AAFBA6FA46777509C996898F61B7A3930E13` |
# Hinge v2.0.1 更新日志

本版集中修复 Android 短信转发的握手时序丢失，以及 Windows 无线 ADB 手动连接后的恢复行为。跨端传输协议保持 `0.1`。

## 修复

- Android 短信/彩信在跨端会话尚未完成身份握手时，不再被当作已发送后静默丢弃。没有可用会话时，最多将 8 条事件暂存于内存；会话可用后再发送。短信正文与验证码不会持久化或写入诊断日志。
- Windows 无线 ADB 的“连接 / 重连”按钮现在会对当前已验证手机执行定向重连，并在重连后再次确认设备状态与身份。该操作可恢复手机系统无线调试页偶发失步的连接标记，不要求重新配对；重连期间剪贴板桥可能短暂中断。
- 手动 ADB 连接成功后显示并保留发现的实际 IP/端口；正常自动恢复和日志提取仍复用健康连接。

## 版本与发行资产

- Android：`2.0.1+122`，包名 `com.hinge.office`，仅 `arm64-v8a`。
- Windows：`2.0.1.0`。
- 发行资产：`Hinge.apk`、`Hinge-Setup.exe`、`Hinge-Windows.zip`。

## 验证结果与限制

- Android 静态分析与 ARM64 Release APK 构建、版本、签名及 ABI 已核验；APK SHA-256：`CDADF55134889874CA7D73A2B68A47EF9E40606ECFF6294FCDFC7524288A7B5D`。
- Windows 无线 ADB 自动化测试 `209/209` 已通过，手动重连代码已在真机连接上核验设备状态与身份；Windows 设置页按钮的安装后 GUI 验收未完成。
- 便携 ZIP 的 810 个条目均可完整读取；内含 `Hinge.dll` 与本次 Release 输出一致，SHA-256 为 `2C051C63F24716E6D84369F30F40401F2167EF42A735AA8BA21285D0C453ECB9`。
- Windows 安装器 FileVersion 为 `2.0.1.0`；外层 Authenticode 未签名，首次运行时 Windows 可能显示来源提示。
- 构建机无法访问 NuGet 漏洞数据源，本次依赖还原使用已有缓存包，未完成在线依赖漏洞检查。
- 本次没有完成 Android→Windows 短信/彩信现场收发验收。

## 发行文件校验值

| 平台 | 文件 | 大小 | SHA-256 |
| --- | --- | ---: | --- |
| Android | `Hinge.apk` | 21,163,270 bytes | `CDADF55134889874CA7D73A2B68A47EF9E40606ECFF6294FCDFC7524288A7B5D` |
| Windows | `Hinge-Setup.exe` | 203,925,053 bytes | `D8C30E19F203B01C1F0A14C4BC03F14F6FEDB28B523F11090F0059B18A3C1505` |
| Windows | `Hinge-Windows.zip` | 135,541,248 bytes | `0517750930AACAD4E284E39C31BD6EBD36BF2951F83EB6D1F38456D490198FE4` |

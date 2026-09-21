# Hinge BLE 唤醒子协议

## 目标

BLE 在 Hinge 中只承担“把已关联的 Android 端叫醒”的低功耗提示，不承担文件传输、剪贴板同步或配对认证。真正的数据通道仍是现有局域网 TCP 会话，现有配对码和会话握手仍是信任边界。

## 广播格式

Windows 使用 `BluetoothLEAdvertisementPublisher` 发布一个 manufacturer-specific data section：

```text
CompanyId       2B  0xFFFE（Hinge 本地私有标识，仅用于 Companion 过滤）
Magic           3B  ASCII "HGW"
Version         1B  0x01
Kind            1B  0x01 pairing / 0x02 file-transfer
DeviceTag       8B  SHA-256(deviceId) 的前 8 字节
```

广播不携带 Windows 配对码、Android 身份、文件名、文件内容或 LAN 地址。当前 Android Companion 只按 `HGW + Version` 过滤，随后由系统绑定的 BLE 地址确定关联设备；`DeviceTag` 可用于日志/后续区分设备，但不是认证材料，也不是当前 Companion 过滤条件。

## 工作流

1. 用户首次配置时，在 Windows 托盘或设置页开启 60 秒配对广播；Android“保活设置”通过 `CompanionDeviceManager` 绑定该 manufacturer data。
2. Windows Explorer 右键发送文件时，先把文件路径写入已有 `pending-file-sends.json` 队列。
3. 若目标 TCP 会话可用，直接沿用现有 FILE_OFFER/FILE_CHUNK/FILE_COMPLETE 传输。
4. 若目标 TCP 不可用，Windows 广播 5 秒 `file-transfer` 唤醒信号，并等待约 8 秒连接恢复。
5. Android 的 `CompanionDeviceService` 只启动已有 `HingeForegroundService`；native broker 立即重试历史可信 Windows peer。连接恢复后，Windows 从原有队列发送文件。
6. BLE、系统 Companion 或局域网恢复失败时，队列保留，通知用户点击 Hinge 常驻通知；点击仍然进入应用并触发 native reconnect。

## 状态与边界

- BLE 唤醒成功不等于 TCP 已连接；UI 和日志必须区分 `wakeup requested`、`connected`、`queued`。
- Android 的 Companion presence 依赖系统扫描和关联设备地址；Windows BLE 广播地址、厂商扫描策略、蓝牙关闭和系统权限都可能使唤醒不可用。
- Windows 主进程继续保持 unpackaged，不能为了 BLE 能力重新引入会破坏 Explorer 稀疏包/主进程身份边界的打包方式。BLE publisher 的 capability 和桌面实际权限必须在目标 Windows 机器上验收。
- 无 BLE、无 Companion、手机被强行停止或厂商阻止后台启动时，持久化队列和 Android 常驻通知是明确兜底，不把失败伪装成在线。

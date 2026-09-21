# 安全会话状态机与加密 (Session Specification v0.1)

## 1. 状态机迁移
```text
Disconnected -> Discovered -> Connecting -> Authenticating -> Connected
Connected <-> Suspended -> Reconnecting -> Disconnected
```

## 2. 会话保持与自愈
- 普通桌面会话周期性发送 `HEARTBEAT_PING`，默认间隔 5 秒。
- Android 作为移动端对端时，Windows 使用 20 秒心跳间隔；连续 3 次未收到任何合法协议帧时进入 `Suspended`，保留 TCP，不因暂时的 Doze/息屏网络静默主动销毁会话。
- Android 原生服务在息屏且连续 180 秒没有入站帧时进入 `Suspended`，继续由原生服务持有连接并发送低频探测；屏幕恢复后以 1 秒间隔探测 15 秒，收到任意合法协议帧即恢复 `Connected`，否则才关闭并重连。
- 只有真实的 socket 读写错误、FIN/RST、协议错误或恢复探测超时才进入 `Reconnecting`/`Disconnected`。
- Android 原生重连采用 2s、5s、10s、30s、60s 退避；成功建立会话或网络切换后的历史重连会清除退避计数。

## 4. 按需唤醒

- 永久 TCP 不是 Android 普通第三方应用的可靠目标。无操作时允许会话进入 `Suspended` 或休眠；文件操作需要实时通道时由 Windows 先发送短暂 BLE 唤醒广播。
- Android `CompanionDeviceService` 只负责从系统 presence 回调启动 `HingeForegroundService`，native broker 再连接历史可信 Windows peer；BLE 不替代 OSP1 握手，也不改变配对码校验。
- Windows 右键发送始终先落盘待发送队列。唤醒窗口内未恢复连接时保持队列，并引导用户点击 Android 常驻通知；连接事件恢复后继续自动发送。

## 3. 会话能力

身份握手可附带 `capabilities` 数组。当前双方都声明
`control-compression-zlib-v1` 时，才启用协议文档中定义的 `COMPRESSED_CONTROL`；
缺少该能力时自动回退到未压缩控制帧，以兼容旧版本客户端。

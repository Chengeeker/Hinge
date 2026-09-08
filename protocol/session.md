# 安全会话状态机与加密 (Session Specification v0.1)

## 1. 状态机迁移
`	ext
Disconnected -> Discovered -> Connecting -> Authenticating -> Connected -> Reconnecting -> Disconnected
`

## 2. 会话保持与自愈
- 周期性发送 HEARTBEAT_PING（默认间隔 5 秒）。
- 若连续 3 次（15 秒）无应答，进入 Reconnecting 状态。
- 重连尝试采用指数退避（1s, 2s, 4s, 最大 10s）。

## 3. 会话能力

身份握手可附带 `capabilities` 数组。当前双方都声明
`control-compression-zlib-v1` 时，才启用协议文档中定义的 `COMPRESSED_CONTROL`；
缺少该能力时自动回退到未压缩控制帧，以兼容旧版本客户端。

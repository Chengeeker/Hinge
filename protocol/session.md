# 安全会话状态机与加密 (Session Specification v0.1)

## 1. 状态机迁移
`	ext
Disconnected -> Discovered -> Connecting -> Authenticating -> Connected -> Reconnecting -> Disconnected
`

## 2. 会话保持与自愈
- 周期性发送 HEARTBEAT_PING（默认间隔 5 秒）。
- 若连续 3 次（15 秒）无应答，进入 Reconnecting 状态。
- 重连尝试采用指数退避（1s, 2s, 4s, 最大 10s）。

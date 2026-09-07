# 剪贴板同步协议规范 (Clipboard Specification v0.1)

## 1. 防循环广播机制 (Loop Prevention)
- 每个剪贴板事件赋予全局唯一 ventId (UUID)。
- 记录发送端 originDeviceId 与生成 	imestamp。
- 本地维护最近已接收的 ventId 环形缓存（LRU 100 项），重复接收立即静默丢弃。

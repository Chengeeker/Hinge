# 文件与数据传输协议 (Transfer Specification v0.1)

## 1. 文件传输协议流
`	ext
Sender                     Receiver
  |                            |
  |--- FILE_OFFER (meta) ----->| (Check free space, permissions)
  |<-- FILE_ACCEPT (offset) ---| (Resume from offset if partial exists)
  |                            |
  |=== FILE_CHUNK (stream) ===>| (Write chunk to disk, flush)
  |                            |
  |--- FILE_COMPLETE --------->| (Verify SHA-256)
  |<-- VERIFIED_ACK -----------|
`

## 2. 内存与分块规范
- 严禁全量读入内存；当前协议默认使用 512 KiB 分块（接收端仍兼容旧版 64 KiB 分块）。
- 接收端采用 .part 临时文件，完整性校验通过后原子重命名。

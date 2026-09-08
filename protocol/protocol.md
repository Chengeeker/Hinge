# Hinge Protocol Specification v0.1 (Single Source of Truth)

> **权威性声明 (SSOT)**：
> 本规约是 Hinge 跨设备通信协议的**最高唯一事实来源 (Single Source of Truth)**。
> 所有客户端代码（Windows .NET 8 / Android Dart）的编解码实现必须严格服从本文档定义。

---

## 1. 协议设计目标与分层模型

Hinge Protocol 采用分层解耦架构：
```text
+-----------------------------------------------------------------+
|                  应用层载荷 (Application Payload)                 |
|   JSON 控制信令 / 16B 遥控事件 / 20B 视频流 / 28B 64KB 文件块     |
+-----------------------------------------------------------------+
|                  传输帧层 (Transport Frame Layer)                |
|             严格 52 字节固定二进制帧头 (Fixed 52-Byte Header)     |
+-----------------------------------------------------------------+
|                  网络传输层 (Transport Abstraction)              |
|        TCP 52831 (控制/可靠流)  /  IStreamTransport (未来UDP/QUIC)|
+-----------------------------------------------------------------+
```

---

## 2. 消息帧基础结构 (Fixed Frame Header - 52 Bytes)

所有通过长连接传输的数据帧，必须以**严格 52 字节**的大端序二进制头作为起始：

```text
 0                   1                   2                   3
 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                       Magic Bytes (4B)                        |
|                     0x4F, 0x53, 0x50, 0x31 ("OSP1")           |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|          Version (2B)         |       MessageType (2B)        |
|             0x0001            |          enum uint16          |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|                     Message ID (16B UUID)                     |
|                                                               |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|                  Timestamp (8B Unix Seconds)                  |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|                     Session ID (16B UUID)                     |
|                                                               |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                      Payload Length (4B)                      |
|                     uint32, Big-Endian                        |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
|                                                               |
|               Payload Data (Variable Length)                  |
|                                                               |
+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
```

### 字段说明：
- **Magic Bytes (4 Bytes)**：`0x4F, 0x53, 0x50, 0x31`（ASCII `"OSP1"`）。
- **Version (2 Bytes)**：协议主版本号，当前固定为 `0x0001`。
- **MessageType (2 Bytes)**：16 位大端序无符号整数，映射业务报文类型。
- **Message ID (16 Bytes)**：RFC 4122 v4 UUID 二进制字节，采用网络字节序（`big-endian`），唯一标识单条消息；发送端不得默认使用全零值。
- **Timestamp (8 Bytes)**：64 位大端序有符号整数，Unix 纪元秒级时间戳。
- **Session ID (16 Bytes)**：会话标识 UUID，未建立会话时全为 0x00。
- **Payload Length (4 Bytes)**：32 位大端序无符号整数，紧随其后的有效载荷字节数；实现必须拒绝负数语义、整数溢出和超过实现上限的载荷。当前实现上限为 16 MiB。
- **总头部长**：`4 + 2 + 2 + 16 + 8 + 16 + 4 = 52` 字节。
- **校验设计**：基础帧头不包含通用 Checksum；各业务载荷自决完整性校验（如大文件与同步清单使用 SHA-256）。
- **解析安全边界**：接收端在分配载荷缓冲区前必须校验长度，当前最大完整载荷为 16 MiB；超过上限的帧直接拒绝。

---

## 3. 消息类型映射表 (MessageType)

| ID (Hex) | 枚举标识 | 载荷格式 | 对应模块 | 说明 |
| :--- | :--- | :--- | :--- | :--- |
| `0x0001` | `DEVICE_DISCOVERY` | JSON | Discovery | UDP 52830 广播声明；发送端同时支持全局广播与各 IPv4 网卡定向广播 |
| `0x0002` | `PAIR_REQUEST` | JSON | Pairing | 设备配对申请 |
| `0x0003` | `PAIR_CONFIRM` | JSON | Pairing | 设备配对确认 |
| `0x0010` | `SESSION_INIT` | JSON | Session | 会话握手发起 |
| `0x0011` | `SESSION_ACK` | JSON | Session | 会话握手确认 |
| `0x0012` | `HEARTBEAT_PING` | Empty | Session | 5秒心跳探测 |
| `0x0013` | `HEARTBEAT_PONG` | Empty | Session | 存活心跳应答 |
| `0x0020` | `TEXT_MESSAGE` | JSON | Transfer | 短文本/URL即时投送 |
| `0x0030` | `FILE_OFFER` | JSON | Transfer | 文件发送邀约与哈希元数据 |
| `0x0031` | `FILE_ACCEPT` | JSON | Transfer | 接受文件邀约并指定断点 offset |
| `0x0032` | `FILE_REJECT` | JSON | Transfer | 拒绝接收文件 |
| `0x0033` | `FILE_CHUNK` | Binary | Transfer | 28B子头 + 64KB标准分块流 |
| `0x0034` | `FILE_COMPLETE` | JSON | Transfer | 文件传输完毕确认与哈希对齐 |
| `0x0035` | `SYNC_MANIFEST_REQ` | JSON | Sync | 请求目录同步清单 |
| `0x0036` | `SYNC_MANIFEST_RESP`| JSON | Sync | 应答目录同步清单 |
| `0x0037` | `SYNC_PULL_REQ` | JSON | Sync | 差量文件拉取请求 |
| `0x0040` | `CLIPBOARD_EVENT` | JSON | Clipboard | 防环路剪贴板数据同步 |
| `0x0050` | `REMOTE_INPUT` | Binary | Remote | 16B 定长遥控键鼠事件 |
| `0x0060` | `SCREEN_STREAM` | Binary | Mirror | 20B 流子头 + 裸 NALU / 信令 |
| `0x0070` | `NOTIFICATION_EVENT` | JSON | Tools | 移动端通知事件广播 |
| `0x0071` | `NOTIFICATION_ACTION`| JSON | Tools | 桌面端通知操作回传闭环 |
| `0x0080` | `TOOL_COMMAND` | JSON | Tools | 原生工具任务调度 |
| `0x0081` | `TOOL_RESULT` | JSON | Tools | 原生工具任务执行结果 |
| `0x0082` | `COMPRESSED_CONTROL` | 二进制封装 | Session/Tools | 双方协商后压缩大控制载荷，内部保留原消息类型 |

### 3.2 可选控制数据压缩

`SESSION_INIT` 和 `SESSION_ACK` 的身份 JSON 可以带有 `capabilities` 数组。
当双方都声明 `control-compression-zlib-v1` 时，发送端才可以把较大的 JSON
控制载荷封装为 `COMPRESSED_CONTROL`；未声明能力的旧客户端继续使用原始消息类型。

`COMPRESSED_CONTROL` 的载荷格式为：

```text
EnvelopeVersion (1B) + Flags (1B, must be 0) + InnerMessageType (2B, big-endian)
+ UncompressedLength (4B, big-endian) + ZLIB payload
```

当前只对工作区、同步、剪贴板、通知和文本等 JSON 控制消息启用，并要求压缩后连同
8 字节封装头确实小于原始载荷；文件分块、屏幕流、握手和心跳不走该路径。
解压后的长度必须等于 `UncompressedLength`，并且不能超过 16 MiB。

### 3.1 工作区工具命令 (`TOOL_COMMAND` / `TOOL_RESULT`)

工作区数据读取和双端配对使用请求-响应 JSON，不新增传输层。请求格式：

```json
{
  "commandId": "uuid",
  "command": "calendarEvents",
  "payload": {}
}
```

结果格式：

```json
{
  "commandId": "uuid",
  "success": true,
  "payload": []
}
```

当前实现的命令为 `getDeviceSummary`、`calendarEvents`、`photos`、`photoBytes`、`files`、`loadNotes`、`saveNote`、`deleteNote`、`loadTasks`、`saveTask`、`deleteTask` 和 `pairDevice`。失败时 `success` 为 `false` 并附带 `error` 文本；权限不足由 Android 平台层返回错误，不伪造空数据。

---

## 4. 专用二进制载荷子规范 (Sub-Payload Specifications)

### 4.1 文件传输分块 (`FILE_CHUNK`, `0x0033`)
- **固定分块大小**：默认标准分块为 **64KB (65,536 Bytes)**。
- **28 字节二进制子头**：
  ```text
  TransferId (16B UUID) + ChunkIndex (4B uint32) + Offset (8B uint64) + ChunkData (0~65536B)
  ```

### 4.2 无线遥控事件 (`REMOTE_INPUT`, `0x0050`)
- **16 字节定长事件**：
  ```text
  ActionType (1B) + Button/Key (1B) + Flags (2B uint16) + DeltaX (4B int32) + DeltaY (4B int32) + Wheel/Timestamp (4B int32)
  ```
- 若 `ActionType == 0x12` (TextInput)，则在 16 字节定长头后直接追加 UTF-8 变长文本流。

### 4.3 视频流数据包 (`SCREEN_STREAM`, `0x0060`)
- **20 字节流式子帧头**：
  ```text
  Subtype (1B) + Flags (1B) + StreamId (2B uint16) + SequenceNumber (4B uint32) + PayloadLength (4B uint32) + TimestampUs (8B uint64 PTS)
  ```
- 视频数据载荷为标准 H.264 Annex-B 裸 NALU（附带 `0x00000001` 或 `0x000001` 起始码）。

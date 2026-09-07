# Screen Stream Protocol Specification (0x0060 SCREEN_STREAM)

## 1. 协议定位与设计目标
`SCREEN_STREAM (0x0060)` 旨在实现 Android 与 Windows 设备间的低延迟屏幕镜像传输。
设计原则：
- **低延迟优先**：去除多余的协议层开销，采用固定 16 字节大端序二进制子头，直接承载 H.264 Annex-B NAL 单元。
- **动态协商机制**：支持双向协商分辨率（480p/720p/1080p）、目标帧率（30/60fps）、码率（1Mbps~8Mbps）及编码格式（H264）。
- **关键帧按需索求 (PLI / Fast Intra Refresh)**：接收端在检测到丢包或初次接入时，可向发送端下发 `request_keyframe`，发送端 MediaCodec 强制产生 IDR 关键帧，实现快速图像恢复。

---

## 2. 二进制子头规范 (20 Bytes Big-Endian)

在标准 52 字节的 `ProtocolFrame` 数据载荷（Payload）中，头部前 20 字节为固定的流子头：

```text
+---------------------+---------------------+---------------------+
| Subtype (1B)        | Flags (1B)          | StreamId (2B uint16)|
+---------------------+---------------------+---------------------+
| SequenceNumber (4B uint32)                | PayloadLength (4B)  |
+-------------------------------------------+---------------------+
| TimestampUs (8B uint64 Big Endian)                              |
+-----------------------------------------------------------------+
| Payload (NAL unit bytes or UTF-8 JSON Control Payload)          |
+-----------------------------------------------------------------+
```

### 2.1 字段说明
| 偏移量 | 字段名 | 类型 | 说明 |
| :--- | :--- | :--- | :--- |
| `0x00` | `Subtype` | `uint8` | 数据子类型（见下文子类型表） |
| `0x01` | `Flags` | `uint8` | 标志位（bit 0: EndOfFrame, bit 1: KeyFrame, bit 2: ConfigFrame） |
| `0x02` | `StreamId` | `uint16` | 当前会话内的流标识号（大端序） |
| `0x04` | `SequenceNumber` | `uint32` | 递增序列号，用于乱序重组与丢包率统计（大端序） |
| `0x08` | `PayloadLength` | `uint32` | 紧随 20 字节头之后的数据体实际长度（大端序） |
| `0x0C` | `TimestampUs` | `uint64` | 捕获/编码微秒时间戳（PTS，大端序） |
| `0x14` | `Payload` | `bytes` | 变长数据：NALU 视频数据或控制 JSON 字符串 |

### 2.2 Subtype 定义表
| 取值 (Hex) | 枚举名称 | 作用 |
| :--- | :--- | :--- |
| `0x01` | `ControlRequest` | 控制命令请求（载荷为 JSON 字符串） |
| `0x02` | `ControlResponse` | 控制命令响应（载荷为 JSON 字符串） |
| `0x10` | `FrameConfig` | 编码器配置集（SPS / PPS，含 Annex-B 起始码 `00 00 00 01`） |
| `0x11` | `KeyFrame` | IDR 关键帧数据（含 Annex-B 起始码） |
| `0x12` | `InterFrame` | 非关键帧数据（P 帧 / B 帧，含 Annex-B 起始码） |
| `0x13` | `JpegFrame` | 兼容模式的 JPEG 屏幕帧；用于 Android MediaProjection 的低负载基线实现 |

当前 Flutter 客户端先使用 `JpegFrame` 完成真实的 Android MediaProjection 到 Windows
接收链路，避免在 H.264 解码器尚未接入时显示假画面。后续可在不改变控制信令的情况下，
将编码器切换为 `FrameConfig` + `KeyFrame` + `InterFrame` 的 H.264 管线。

---

## 3. 控制流协商信令 (Control JSON Payloads)

### 3.1 启动流请求 (`start`)
```json
{
  "action": "start",
  "width": 1280,
  "height": 720,
  "fps": 30,
  "bitrate": 2500000,
  "codec": "H264"
}
```

### 3.2 启动流响应 (`accepted` / `rejected`)
```json
{
  "status": "accepted",
  "streamId": 1,
  "width": 1280,
  "height": 720,
  "fps": 30,
  "bitrate": 2500000,
  "codec": "H264"
}
```

### 3.3 请求关键帧 (`request_keyframe`)
```json
{
  "action": "request_keyframe",
  "streamId": 1
}
```

### 3.4 停止流 (`stop`)
```json
{
  "action": "stop",
  "streamId": 1,
  "reason": "user_cancelled"
}
```

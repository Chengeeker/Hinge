# Hinge Cloud Relay 协议（v1）

Cloud Relay 是 Hinge 的可选、异步文件中转通道。它不替换现有的 LAN TCP
`FILE_OFFER → FILE_ACCEPT → FILE_CHUNK → FILE_COMPLETE` 协议；LAN 会话可用时，
发送仍然走原有路径。只有用户在设置中开启自己的 Worker、目标设备已经在
Hinge 中被信任且两端都完成 Relay 注册时，文件才允许走 Cloud Relay。

当前实现位于独立项目 `Hinge-Relay`：Cloudflare Worker 只处理认证、R2
multipart 和生命周期，明文文件名、大小、摘要和文件内容都在客户端加密后才
离开设备。Relay 不支持互联网首次配对，也没有 Hinge 官方账号或中央文件服务。

## 1. 路由和状态

```text
发送文件
  ├─ 可用 LAN Session → 现有 Hinge TCP 文件传输
  └─ 无 LAN Session + Cloud Relay 已配置
       → 客户端加密 → Worker/R2 暂存 → 接收端轮询 → 解密/校验 → ACK
```

Cloud Relay 发送记录使用现有传输历史模型，并新增 `AwaitingPickup` 状态：

```text
queued → transferring → awaitingPickup → completed
                                  └──────→ failed
```

接收端先用共享 Relay 密钥把 manifest 的发送方 ID 映射回本地可信设备；不在可信
设备集合中的 manifest 不会被下载。之后只有在 AES-GCM、文件大小和明文 SHA-256
全部通过后才移动最终文件并发送 ACK。Worker 收到 ACK 后删除 inbox manifest 和加密 blob，同时为发送端写入一次
`delivered` receipt。发送端轮询 receipt 后把 `AwaitingPickup` 标记为完成。

Android/Windows 的轮询是尽力而为：Android 被系统完全停止时，不承诺即时唤醒；
manifest 在 TTL 内保留，下一次 Hinge 启动或轮询时仍可下载。

跨设备剪贴板同步已从 Hinge 客户端移除；当前 Relay API 仅支持文件传输与设备注册。旧版 Worker 需要重新部署当前 `Hinge-Relay` 源码后才会关闭历史 `/v1/clipboard` 路由。

## 2. 设备标识、信任与认证

每台设备使用 Hinge 已有的长期 `DeviceId`。用户在两台已经互相信任的设备上手动
导入同一个 32 字节 `RelayEncryptionKey`，客户端计算：

```text
relayDeviceId = base64url(
  HMAC-SHA256(RelayEncryptionKey,
    "Hinge-Relay-Device-v1|" + HingeDeviceId)
)
```

Worker 不知道 Hinge `DeviceId` 或 Relay 密钥，只保存 `relayDeviceId` 和注册后
返回的随机设备 Token 哈希。设备注册需要管理员 Token；日常 API 需要：

```http
Authorization: Bearer <deviceToken>
X-Hinge-Relay-Device: <relayDeviceId>
```

注册接口：

```http
POST /v1/register
```

该接口只用于首次注册或重新生成设备 Token。管理员 Token 不写入配置文件，也不
由 Hinge 持久化；设置页注册成功后清空输入框。

## 3. 客户端加密格式

### 3.1 Transfer key

每个 transfer 使用独立的 32 字节密钥：

```text
salt = SHA-256(UTF-8(transferId))
info = UTF-8(
  "Hinge-Cloud-Relay-v1|" + senderRelayDeviceId + "|" + receiverRelayDeviceId)
transferKey = HKDF-SHA256(
  ikm=RelayEncryptionKey, salt=salt, info=info, length=32)
```

### 3.2 文件分块

- 明文分块大小固定为 `8 MiB`；空文件仍有一个空明文分块。
- 每一块使用 AES-256-GCM；R2 multipart 的 part 内容是 `ciphertext || tag`，其中
  `tag` 固定 16 字节。
- nonce 为 `SHA-256(UTF-8(transferId))[0..7] || uint32_be(partNumber)`，长度 12
  字节；part 编号从 1 开始。
- AAD 为以下 UTF-8 字符串：

```text
Hinge-Cloud-Relay-v1|transferId|senderRelayDeviceId|receiverRelayDeviceId|partNumber|plaintextLength
```

manifest 的 `ciphertextSize` 为明文文件大小加上 `16 × partCount`。Worker 可以
按 part 范围从 R2 返回加密数据，但不能解密或重排数据。

### 3.3 加密 manifest 元数据

文件名、MIME、明文大小、修改时间和 SHA-256 序列化为 JSON 后，使用同一个
`transferKey` 加密。metadata nonce 为：

```text
SHA-256(UTF-8("Hinge-Cloud-Relay-v1|metadata|" + transferId))[0..11]
```

metadata AAD 为：

```text
Hinge-Cloud-Relay-v1|metadata|transferId|senderRelayDeviceId|receiverRelayDeviceId
```

manifest 仅保存 `metadataCiphertext`、`metadataNonce` 和 `metadataTag` 的
base64url 字符串。Worker 需要校验 manifest 结构，但不读取其中的明文元数据。

## 4. Worker API v1

| 方法和路径 | 用途 | 身份 |
| --- | --- | --- |
| `GET /v1/health` | 健康检查和版本 | 无 |
| `POST /v1/register` | 注册/重置设备 Token | 管理员 Token |
| `GET /v1/devices/{relayDeviceId}` | 检查目标是否已注册 | 设备 Token |
| `POST /v1/transfers` | 创建 R2 multipart upload | 发送设备 |
| `PUT /v1/transfers/{id}/parts/{n}` | 上传加密 part | 发送设备 |
| `POST /v1/transfers/{id}/complete` | 校验 ETag 并发布 inbox manifest | 发送设备 |
| `GET /v1/inbox` | 获取接收端待处理 manifest | 接收设备 |
| `GET /v1/transfers/{id}/parts/{n}` | 按范围下载加密 part | 接收设备 |
| `POST /v1/transfers/{id}/ack` | 删除 blob/manifest 并创建 receipt | 接收设备 |
| `GET /v1/receipts` | 查询已送达 transfer | 发送设备 |
| `DELETE /v1/receipts/{id}` | 删除已消费 receipt | 发送设备 |

R2 key 只使用经过校验的 Relay ID 和 UUID transfer ID：

```text
devices/{relayDeviceId}.json
transfers/{senderRelayDeviceId}/{transferId}.json
blobs/{receiverRelayDeviceId}/{transferId}.bin
inbox/{receiverRelayDeviceId}/{transferId}.json
receipts/{senderRelayDeviceId}/{transferId}.json
```

R2 bucket 必须保持私有，Worker 是唯一访问入口。过期 inbox 和未完成 multipart
upload 由定时清理任务删除；默认 TTL 为 168 小时，部署变量最多允许 30 天。

## 5. 兼容与限制

- `API_VERSION=1`、`CONFIG_SCHEMA_VERSION=1`；任何字段或加密格式变化都必须先
  提升版本并保留旧格式的迁移/拒绝策略。
- Cloud Relay 不修改 `protocol/protocol.md` 或 LAN 帧编号，因此旧版 Hinge 仍可
  通过 LAN 互传；旧版客户端不会识别 Cloud Relay 设置，也不会读取 Relay 文件。
- 初版不实现 LAN 中断后的无缝续传；一次 Cloud Relay upload 失败时，发送记录会
  保留失败状态，接收端不会看到未完成 manifest。
- transfer 提交到 inbox 后，初版没有撤回/远端删除 API；发送端可以保留历史记录，
  文件会在接收端 ACK 或 TTL 清理前继续存在。需要撤回语义时必须新增经过认证的
  sender-side delete，并同步更新状态机和测试。
- inbox/receipt 查询当前按页返回，客户端需要在后续版本支持 cursor 才能处理超过
  单页上限的长期积压任务。
- Worker 不能保证接收端被操作系统强制停止时运行下载任务；这属于 Android
  生命周期边界，不是 Cloudflare API 的传输失败。

## 6. 变更检查清单

修改此协议前必须同时检查：

1. `android/lib/core/cloud_relay.dart` 与 `windows/Hinge.Core/CloudRelayCrypto.cs`
   的字节级结果是否一致；
2. `Hinge-Relay/src` 的 API 校验、R2 生命周期和回执删除语义；
3. `CloudRelay` 设置、可信设备和现有 `TransferHistoryStore` 状态机；
4. Worker 测试、Windows .NET 测试、Flutter analyze/test 和真实双端验收边界；
5. `D:\App\开发文档\Hinge.md` 的实现记录和已知限制。

# 设备配对与安全信令协议规范 (Pairing Specification v0.2)

> **权威性声明 (SSOT)**：
> 本规约是 Hinge 设备间相互认证、配对与信任建立的权威协议规范。
> 严禁使用未认证的伪随机 PIN 码。所有配对认证必须遵循真实随机挑战、屏幕比对核验与公钥指纹绑定。

---

## 1. 配对交互时序模型 (4-Step Interactive Pairing Sequence)

```text
[发起端 A (手机/电脑)]                                 [接收端 B (电脑/手机)]
       │                                                       │
       │────── 1. PAIR_REQUEST (0x0002) ──────────────────────>│
       │   - SessionId                                         │
       │   - InitiatorDeviceId, InitiatorName                  │
       │   - InitiatorNonce (16B CSPRNG Hex)                   │
       │   - InitiatorPublicKey (PEM/Hex)                      │
       │                                                       │
       │                                   (双方独立派生动态 6 位 SAS 随机码)
       │                                                       │
       │<───── 2. PAIR_CHALLENGE / PAIR_CONFIRM (0x0003) ──────│
       │   - SessionId                                         │
       │   - ReceiverDeviceId, ReceiverName                    │
       │   - ReceiverNonce (16B CSPRNG Hex)                    │
       │   - ReceiverPublicKey (PEM/Hex)                       │
       │                                                       │
 [双端屏幕大字体显示 6 位 SAS 随机码]                [双端屏幕大字体显示 6 位 SAS 随机码]
       │                                                       │
       +════════════════════ 用户人工肉眼比对并点击确认 ════════════════════+
       │                                                       │
       │────── 3. PAIR_CONFIRM (Accepted = true) ─────────────>│
       │                                                       │
       │<───── 4. PAIR_ACK (Accepted = true, AuthToken) ───────│
       │                                                       │
 [写入 TrustStore (持久化公钥指纹)]                [写入 TrustStore (持久化公钥指纹)]
```

---

## 2. 确定性短身份认证码 (SAS) 计算公式

双端在交换双方随机数（`Nonce_A`, `Nonce_B`）及公钥后，按照以下确定性算法独立计算 6 位十进制验证码：

```text
ContextInfo = "Hinge-SAS-v1" || Nonce_A (16B) || Nonce_B (16B) || PubKey_A || PubKey_B;
Digest = HMAC_SHA256(Key = SharedSecret, Data = ContextInfo);
TruncatedInt = ToUInt32BigEndian(Digest[0..3]);
SAS_Code = (TruncatedInt % 900000) + 100000;
```
- **输出范围**：严格介于 `100000` 至 `999999` 之间的 6 位十进制数字字符串。
- **安全保障**：由于局域网中间人无法在不被察觉的情况下篡改双端各自生成的 Nonce 与公钥，任何篡改都会导致双端屏幕显示的 6 位数字不匹配，用户肉眼比对即可阻断中间人劫持（MITM）。

---

## 3. 报文载荷结构 (JSON Payloads)

### 3.1 发起配对 (`PAIR_REQUEST`, `0x0002`)
```json
{
  "initiatorDeviceId": "c85d7b5f-519b-4e12-8e10-3b0222a7f05a",
  "initiatorName": "Alice's Phone",
  "receiverDeviceId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "initiatorNonce": "a1b2c3d4e5f60718293a4b5c6d7e8f90",
  "initiatorPublicKey": "PUBKEY_BASE64_OR_HEX",
  "timestamp": 1756992000
}
```

### 3.2 响应与确认配对 (`PAIR_CONFIRM`, `0x0003`)
```json
{
  "initiatorDeviceId": "c85d7b5f-519b-4e12-8e10-3b0222a7f05a",
  "receiverDeviceId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "receiverName": "Bob's PC",
  "receiverNonce": "09f8e7d6c5b4a39281706f5e4d3c2b1a",
  "receiverPublicKey": "PUBKEY_BASE64_OR_HEX",
  "accepted": true,
  "sasCode": "748291",
  "timestamp": 1756992002
}
```

---

## 4. 信任固化与持久化规约 (TrustStore Binding)
配对确认成功后，双端必须将对端的长期凭据持久化到本地：
- `deviceId`: 设备唯一 UUID
- `name`: 设备别名
- `publicKey`: 设备长期公钥（或派生指纹 Fingerprint）
- `pairedAt`: 配对成功 UTC 毫秒时间戳
- `trustState`: `Trusted`

后续建立长连接会话（`SESSION_INIT`, `0x0010`）时，连接方必须附带使用私钥对会话挑战码的签名证明其持有对端 TrustStore 中记录的公钥，否则拒绝建立安全会话。

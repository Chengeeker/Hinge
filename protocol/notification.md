# Notification Relay & Native Tools Specification (0x0070 / 0x0071 / 0x0080 / 0x0081)

## 1. 协议设计目标
提供跨设备通知流转与离线原生轻量办公工具集调度通道：
1. **通知流转 (`0x0070 NOTIFICATION_EVENT`)**：将移动端通知推送至桌面端，附带包名、标题、内容与回复能力。
2. **快捷动作与回复 (`0x0071 NOTIFICATION_ACTION`)**：桌面端对通知进行点击交互（如快捷输入文字回复、标为已读、清除通知）并实时回传给移动端。
3. **原生工具指令 (`0x0080 TOOL_COMMAND` / `0x0081 TOOL_RESULT`)**：调度双端离线轻量工具（如 PDF 页面拆分/合并、离线 OCR 识别）。

---

## 2. 报文载荷格式 (JSON Schema)

### 2.1 NOTIFICATION_EVENT (`0x0070`)
```json
{
  "notificationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "packageName": "com.tencent.mm",
  "appName": "WeChat",
  "title": "Alex",
  "content": "Let's review the document.",
  "timestamp": 1725450000000,
  "canReply": true,
  "actions": ["reply", "dismiss"]
}
```

### 2.2 NOTIFICATION_ACTION (`0x0071`)
```json
{
  "notificationId": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "actionKey": "reply",
  "replyText": "Received, thanks!",
  "timestamp": 1725450005000
}
```

### 2.3 TOOL_COMMAND (`0x0080`) & TOOL_RESULT (`0x0081`)
- **TOOL_COMMAND**:
  ```json
  {
    "commandId": "cmd-1234",
    "toolType": "ocr_recognize",
    "parameters": { "language": "en-US" }
  }
  ```
- **TOOL_RESULT**:
  ```json
  {
    "commandId": "cmd-1234",
    "success": true,
    "resultText": "Recognized Text Content...",
    "errorMessage": null
  }
  ```

---

## 3. 过滤规则与安全防护 (Filter & Anti-Spam)
- **去重防抖 (Debounce Window)**：同一应用在 2 秒内针对同一标题和内容的连续弹窗自动抑制。
- **持久常驻通知抑制 (Persistent Suppression)**：自动过滤电池充电、后台保活服务、VPN 状态等无交互意义的常驻通知。
- **白名单/黑名单过滤 (Whitelist / Blacklist)**：支持按包名（`packageName`）开启定向通知推送或全面阻断。
- **鉴权要求**：仅受信配对设备（`TrustStore.isTrusted`）允许推送通知或触发快捷回复。

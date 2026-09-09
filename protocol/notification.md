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
  "source": "generic",
  "isVerificationCode": false,
  "timestamp": 1725450000000,
  "canReply": true,
  "actions": ["reply", "dismiss"]
}
```

### 2.1.1 Android 短信事件

短信同步复用 `NOTIFICATION_EVENT`，不新增跨端帧。Android 仅在用户主动开启短信同步、并且存在已建立的可信会话时发送新到的 SMS：

```json
{
  "notificationId": "sms-event-id",
  "packageName": "android.provider.Telephony.SMS",
  "appName": "短信",
  "title": "1069xxxx",
  "content": "你的验证码是 123456，请勿泄露。",
  "source": "sms",
  "isVerificationCode": true,
  "verificationCode": "123456",
  "timestamp": 1725450000000,
  "canReply": false,
  "actions": ["copy_code"]
}
```

- `source` 为 `sms` 或 `mms`；普通通知保持为空或使用 `generic`；
- `verificationCode` 只在正文包含验证码语义且匹配到 4–8 位数字时出现；
- Android 不扫描历史收件箱、不把短信正文写入文件；`RECEIVE_SMS` 用于实时广播，`READ_SMS` 只作为部分设备/验证码广播的兼容回退，并从权限获得后才开始观察新插入的收件箱记录；
- 非默认短信应用通常只能可靠收到彩信到达广播，不能从广播中取得通用的彩信正文，因此 `mms` 事件只提示“收到彩信”，不伪造正文；
- Windows 对 `sms`/`mms` 使用托盘气泡通知；只有被识别为验证码的短信/彩信，点击整条气泡才会在 Windows 进程内根据 `notificationId` 查找验证码并写入本机剪贴板，不通过 `NOTIFICATION_ACTION` 把验证码回传手机。普通短信和其他通知不会触发复制。

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

### 4.1 短信隐私边界

短信属于高敏感个人数据。短信同步默认关闭；开启前需要 Android 用户明确授予 `RECEIVE_SMS`，并会引导申请 `READ_SMS` 兼容权限，只有已建立的可信 Windows 会话会接收事件。Hinge 只观察授权后的新消息，不通过读取短信数据库扫描历史；系统或安装来源拒绝高敏感权限时，会回退到系统允许的实时广播路径。

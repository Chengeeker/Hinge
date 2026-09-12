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

### 2.4 通知历史工具命令

通知历史不新增传输帧，使用工作区的 `TOOL_COMMAND` / `TOOL_RESULT` 请求-响应模型。
Android 端在用户授予通知访问权限并主动开启“通知历史”后，将收到的应用通知写入应用私有 SQLite；
Windows 端按页读取，不把通知历史复制到公共文件目录。

读取请求：

```json
{
  "commandId": "uuid",
  "command": "notificationHistory",
  "payload": {
    "offset": 0,
    "limit": 100,
    "ascending": true,
    "packageName": ""
  }
}
```

读取结果：

```json
{
  "commandId": "uuid",
  "success": true,
  "payload": {
    "access": true,
    "enabled": true,
    "total": 123,
    "items": [
      {
        "id": "com.tencent.mm|key|1725450000000",
        "packageName": "com.tencent.mm",
        "appName": "微信",
        "title": "联系人",
        "content": "通知正文",
        "timestamp": 1725450000000,
        "category": "msg",
        "ongoing": false,
        "notificationKey": "0|com.tencent.mm|…",
        "iconBase64": "…"
      }
    ],
    "applications": [
      {
        "packageName": "com.tencent.mm",
        "appName": "微信",
        "count": 42,
        "iconBase64": "…"
      }
    ]
  }
}
```

桌面端点击历史记录使用 `notificationHistoryAction`。打开动作的载荷至少包含
`action: "open"`、`packageName` 和 `notificationKey`。对于微信/QQ，Windows 端只负责检查
`Weixin.exe`/`WeChat.exe`/`QQ.exe`/`QQNT.exe` 是否已经运行，并只唤醒匹配进程的主窗口；不枚举或操作子窗口，
不发送 `weixin://`/`mqq://` 深链，也不回传 Android 原通知入口，避免客户端进入额外登录、聊天或渲染界面。
如果客户端没有运行，Windows 才从已发现的安装路径启动它；找不到客户端时安全失败，不弹出 Windows 的“获取打开此链接的应用”对话框，也不伪造聊天定位。

删除动作使用同一个工具命令，单条删除只需传记录主键：

```json
{
  "commandId": "uuid",
  "command": "notificationHistoryAction",
  "payload": {
    "action": "delete",
    "id": "com.tencent.mm|key|1725450000000"
  }
}
```

清空动作传 `action: "clear"`，Android 返回实际删除数量；Windows 和 Android 页面在动作完成后重新读取
总数、分页和应用筛选统计。删除只影响 Hinge 应用私有通知历史数据库，不撤回系统通知，也不删除微信/QQ原消息。

---

## 3. 过滤规则与安全防护 (Filter & Anti-Spam)
- **实时转发过滤**：`NOTIFICATION_EVENT` 继续使用通知管理器的去重、常驻通知抑制和包名白名单/黑名单规则。
- **历史记录范围**：通知历史页默认不套用实时转发过滤，以免用户在手机上收到的记录被静默遗漏；只跳过 Hinge 自身通知以及标题和正文都为空的无内容通知。
- **分页边界**：`limit` 由服务端限制在 1–200，`offset` 不限制总记录数；总数单独返回，客户端按页加载。
- **鉴权要求**：历史命令只能在已经建立的受信会话中执行，通知访问权限和“通知历史”开关均由 Android 用户主动控制。

### 4.1 短信隐私边界

短信属于高敏感个人数据。短信同步默认关闭；开启前需要 Android 用户明确授予 `RECEIVE_SMS`，并会引导申请 `READ_SMS` 兼容权限，只有已建立的可信 Windows 会话会接收事件。Hinge 只观察授权后的新消息，不通过读取短信数据库扫描历史；系统或安装来源拒绝高敏感权限时，会回退到系统允许的实时广播路径。

### 4.2 通知历史隐私边界

通知历史默认关闭。Android 系统通知访问授权是独立于 `POST_NOTIFICATIONS` 的高敏感授权，用户需要在
系统设置中明确允许 Hinge 访问通知，并在“工作区 > 通知历史”中开启采集。只在授权和开关都开启后记录
新到或当前仍活动的通知；正文、包名和原通知 key 保存在 Hinge 应用私有数据库，不写入共享存储和日志。
关闭采集不会删除已有历史，但会停止后续写入。历史记录打开只回传记录标识、包名和通知 key，
Android 端再尝试使用原通知入口；删除/清空只传记录 ID 或清空动作，不把正文写入日志或公共存储。

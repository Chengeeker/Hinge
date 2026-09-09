# 系统架构

> 本文描述当前仓库的实际边界，不把历史设计稿或 Mock Provider 当成已交付的产品能力。

## 1. 总体分层

```text
UI / Workspace
    |
Feature orchestration
    |
Hinge Core: device, session, request routing, transfer state
    |
Hinge Protocol: discovery, framing, session, transfer, workspace data
    |
Platform adapters
    |                         |
Android native bridge       Windows App SDK / Win32
    |                         |
Android OS                   Windows OS
```

两端的 UI 不直接操作 Socket，也不直接拼装协议帧。页面向 Feature/Core 请求数据，Core 根据当前有效设备会话发送命令，再把结果转换成 UI 可消费的模型。

## 2. 客户端边界

### Android

- Flutter 负责页面、导航、主题、分页列表和工作区交互；
- Android 原生 `MethodChannel` 负责 `StatFs`、`CalendarContract`、`MediaStore`、通知、前台服务、短信接收/访问权限和系统设置跳转；短信广播以及获得访问权限后的新收件箱观察通过进程内 EventChannel 进入 Dart，再复用通知协议；
- 前台服务使用 `connectedDevice` 类型、Wi-Fi/组播锁和 `START_STICKY`，但不承诺绕过厂商电池策略；
- Monet 动态取色只在系统提供公开 `system_*` 颜色角色时应用，关闭后回退到预置主题。

### Windows

- .NET 8 + Windows App SDK + WinUI 3；
- `NavigationView` 提供独立功能页，`Window.AppWindow` 管理窗口大小、标题栏和材质；
- Win32 平台层负责托盘、通知、窗口和系统文件关联；
- 文件管理和相册使用分页、取消旧请求、缩略图预算和 UI 线程外的网络读取；
- 图片、视频、音频通过系统默认关联应用打开，避免在应用内维持第二套媒体生命周期。

## 3. 连接和请求路由

1. UDP `52830` 广播或定向探测发现设备；
2. TCP `52831` 建立会话并完成身份状态同步；
3. 设备注册表记录名称、品牌、地址、能力和最近在线时间；
4. 会话管理器只有在身份握手完成后才把连接暴露给功能页；
5. 所有功能请求通过当前有效连接的统一调度器发送，避免首页和功能页持有不同的旧 Socket；
6. 断线时请求失败并回收，设备回到发现/重连状态，不把旧连接继续当作可用连接。
7. 身份握手协商可选的控制数据压缩；不支持压缩的旧客户端继续使用原始控制帧。

## 4. 文件和媒体数据流

- Android 侧按 `offset` / `limit` 查询目录和媒体数据，并返回总数；
- Windows 首批读取 200 项，滚动接近末尾时请求下一页；
- 缩略图使用独立预算和取消令牌，旧页面切换后不再把结果写回当前页面；
- 大文件传输使用分块模型，不依赖一次性把整个目录或媒体库装进 UI；
- Windows 外部文件拖入由窗口 Win32 层接收最终 `WM_DROPFILES` 投放事件；窗口内的悬停提示由短周期指针轮询辅助更新，避免等待鼠标松开才显示目标；
- 拖拽状态明确分为应用内本地手势、应用内远程文件拖出、窗口移动和外部拖入。只有外部拖入才进入文件投放预览；本地指针按下、远程文件拖出以及标题栏移动不会污染外部拖入状态；
- Win32 层同时读取前台 GUI 线程的 `GUI_INMOVESIZE` 标志，过滤拖动或调整其他程序窗口时产生的鼠标悬停；资源管理器文件拖入不会被该标志过滤；
- 外部文件拖放仍受 Windows 窗口权限级别约束，应用层不能绕过 UAC 的跨权限拖放限制。

## 5. 协议文档边界

`protocol/` 是跨端协议的单一事实来源，当前主要文件为：

- `protocol.md`：帧结构、消息类型和公共字段；
- `session.md`：会话生命周期和连接状态；
- `transfer.md`：文件传输、分块和校验；
- `clipboard.md`：剪贴板同步；
- `notification.md`：通知模型；
- `screen_stream.md`：实验性投屏模型，仅供后续原生实现参考。

如果修改命令、字段或分页模型，必须同时更新 Android、Windows、协议文档和测试向量。

## 6. 当前不属于生产闭环的模块

真实投屏编码/解码、通知回复、OCR、完整双端加密握手和所有 Android 厂商后台策略，不能仅凭 Mock 或 localhost 回环测试判定完成。相关入口应明确显示未接入或需要继续验收。

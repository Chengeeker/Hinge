# 手机查看并控制 Windows 桌面实施检查点

更新时间：2026-10-29（北京时间）。本文件是持续开发任务的工作检查点；项目规范仍以 `D:\App\开发文档\Hinge.md` 为准。

## 用户授权与运行约束

- 目标：手机显示电脑桌面并操控，低延迟、模拟鼠标、完整手势和清楚的两端设置/运行状态。现有触控板/游戏模式必须保留。
- 用户睡觉期间禁止 ADB、手机唤醒、连接、安装或操作。离线编译不需要手机；真机验收等用户回来。
- 开始及开发阶段边界调用账号 usage limits；5 小时剩余 <=5% 时保存进度后停止，刷新再继续。额度是账号共享百分比，不能等同于时间/精确 token。不要使用免费额度重置券。
- 2026-10-29 00:00 首次查询剩余44%，刷新 01:16:35；随后查询剩余41%。自动续作 heartbeat id `hinge`，每30分钟检查并推进同一聊天。
- 未经请求不推送/发布 GitHub、不安装候选、不更改系统防火墙/证书/安全策略。APK 主包、arm64-v8a；Windows 构建安装器。保留回退包。

## 已完成组件（还不是完整远控）

1. `windows/Hinge.Core/DesktopVideoProtocol.cs`：独立媒体流 HDV1、32字节大端头、Annex B 完整访问单元；H264/H265标识、配置/关键帧标记、序号、微秒PTS、宽高。尺寸偶数2..8192，帧体1..4MiB，验证后再分配，截断报错。读写可取消，写入由单一发送任务串行拥有。
2. `android/.../DesktopVideoProtocol.kt`：相同头部/限长解析；不经 Flutter 传递视频字节。
3. `android/.../DesktopVideoDecoder.kt`：MediaCodec Surface 解码组件；Android 11+ 请求低延迟；分开统计提交/解码/OnFrameRendered回调。分辨率/codec变化重建解码器；不承诺解码器实际硬件加速或光子级显示延迟。组件尚未挂入 MainActivity 和 Dart 页面。
4. C#两项定向测试通过：2字节拆包流往返/EOF、恶意长度与截断拒绝；`tmp/desktop-video-tests.log`，0警告0错误。Android `:app:compileReleaseKotlin`成功（1m5s），日志 `tmp/desktop-video-kotlin.log`；现有文件保留既有弃用警告，本轮新文件未报编译错误。没有操作手机或交付未接通的新安装包。

## 下一个可执行阶段

1. 完成 Windows 采集/编码发送器。优先系统 Windows.Graphics.Capture / DXGI + Media Foundation H.264 硬编，或检查现有 LGPL FFmpeg 9.0.2 DLL 的可复用编码能力；不要复制 UU GPL 代码，也不要默认新加巨型外部运行时。先构建可运行的本机采集编码验证，再做端到端接线。没有检测成功前不声称NVENC或硬件编码。
2. 专用媒体连接与身份认证：视频不能走现有大帧业务队列。Windows 用户明确开启桌面共享/授权，拒绝默认暴露屏幕；复用远控会话所有权，断线/禁用/超时及时释放。现有信任ID可伪造，不能把经明文控制通道发出的TLS指纹称为防MITM认证；必须实现真正绑定的首次确认与持久凭证，避免每次复制密钥。不降低已有显式加密配置。
3. Native Texture/Surface接线和TLS接收任务，失败/取消/切后台可靠清理；后台不继续泄漏电脑画面。读取/解码/呈现分开统计；限制缓冲，发生拥塞不能随意丢弃P帧后继续解码，要请求关键帧并重新同步。
4. Flutter桌面页面：设置里的原“手机远控电脑”入口，桌面/触控板选择，横竖屏、适应/原比例、缩放/平移、虚拟鼠标和左右键。单指移动/点击、双指滚动/右键、双击按住拖拽，手势互斥；多指与游戏控件不会卡键。共享输入发送器，不创建互相抢占的第二个控制会话。
5. 编码/网络参数默认60fps、可调清晰度/码率，先保证H264路径。H265仅双方实际支持再启用。不要把接收FPS当显示FPS，不以追求120数字增加队列延迟。
6. Windows设置清晰显示共享状态、观看设备、停止按钮；多显示器选择与旋转/缩放坐标映射；鼠标相对/绝对模式明确。避免UI线程做采集/编码/网络等待。
7. 必要测试包括协议限长/分片、认证重放拒绝、连接所有权与取消、画面尺寸变化、坐标letterbox/缩放、手势互斥和释放；构建Windows安装器与ARM64主APK，检验签名/ABI/载荷。真实视频时延和手势体验等用户可用时验收，不能用模拟测试冒充。

## 2026-10-29 桌面共享开关升级后恢复

- 根因：设置页把开关直接绑定到进程内的 `DesktopMediaServer != null`，没有记录用户的开启偏好；主窗口退出会停止服务器，升级后新进程自然显示关闭。桌面视频参数此前也只保存在 `ApplicationData.LocalSettings`，而项目历史已记录包身份更新可能清除此存储。
- 修复：用户显式开启后把偏好写入 `HKCU\Software\Hinge`，下次启动仅在此前明确开启时恢复共享；默认仍关闭。显式关闭、停止按钮和清除设备身份都会保存关闭。启动恢复失败时保留开启偏好并显示错误，关闭后重新开启可重试。
- 桌面视频参数沿用现有 JSON 格式，注册表作为稳定主存储，`LocalSettings` 只作兼容镜像；首次读取旧 LocalSettings 时迁移到注册表。
- 安全边界：自动恢复仅重启本机桌面媒体服务，仍要求已配对且当前 Hinge 会话已连接的 Android 设备通过现有 TLS 身份验证；未曾开启的用户不会开放共享。
- 离线构建与包/签名/回退核验结果追加在 `D:\\App\\开发文档\\Hinge.md` #419。没有执行跨端实机验收。

## 本机环境错误处理

- `dotnet test`首次静默非零，但重试并重定向日志后编译/测试成功。只重复有失败证据的命令，勿反复全量测试。
- Gradle/JDK17 `Unable to establish loopback connection`：仅preferIPv4Stack仍失败，按Hinge.md #366附近记录，把当前构建进程TEMP/TMP和java.io.tmpdir置于项目tmp下的长路径后已进入实际Gradle任务。不要全局修改用户环境，也不要终止无关Java/Gradle进程。
- 当前构建任务session id在聊天工具记录；等待结果后更新本文件，不要丢失失败日志。

## 安全与验收边界

桌面视频尚未接通；不能把新增协议/解码类描述为已支持桌面远控。旧Release产物仍供回退，尚未被本阶段覆盖。以前#382明确的认证问题必须落实到代码，而非重复写可行性分析。

## 2026-10-29 当前接线结果（优先读取，替代上方早期状态）

- Windows原生DXGI/MF组件、NativeDesktopVideoSource、DesktopMediaServer、用户保护的服务端证书/peer pin、双端64bit身份码与首次批准均已实现。主程序设置页已接共享开关/状态/停止/清除手机身份，0x0084描述端口已接主消息处理。原生helper已加入WinUI构建发布Content。DesktopClientIdentity与DesktopVideoBridge已挂MainActivity，原生TLS/Surface/解码/后台停止已接。未知0x0084帧已确认由Android broker通用frame事件转发，无需另加白名单。
- Android设置电脑卡片明确提供“电脑桌面”和“打开触控板”两个按钮；DesktopVideoView请求描述、核对身份码、显示Texture/输出统计，复用TouchpadScreen单指/双指/拖拽与文本输入；独立缩放模式不发鼠标，横屏使用紧凑工具栏。视频通过GlobalKey跨横竖屏保留。进入后台原生断流，触控板直接取消手势释放拖拽。
- 实际合成测试：NVIDIA H264硬编150帧/2.5秒，PTS59.97FPS/最大18.09ms；软件150帧/60.0FPS/最大18.08ms；两条均经系统H264解码器完整解出150帧。不是手机实测。原生使用高精度waitable timer，避免Sleep1造成约32FPS。CPU兼容颜色转换、缩放和简化实际坐标光标仍在，不是GPU零复制。
- 4项.NET定向测试通过：分帧/限长、身份码绑定、TLS首次确认前无画面/确认后传帧/同证书复用不重配/换证书拒绝。Flutter桌面页2项通过，原触控板3项通过，Dart目标analyze无问题。原生MF软件/硬件输出验证脚本位于scripts/test_desktop_video.py。Schannel不支持EphemeralKeySet，已改UserKeySet导入（不要回退）；PFX仍用户保护加密存储，不改系统信任根。视频输出统计来源OnFrameRendered，UI已改称“视频输出”，不是屏幕物理刷新率。
- 末次Windows完整发布成功，838项ZIP：161448052 bytes SHA256 53EE85C83DDC47DD8069E495E8C71A9C0C8D8787A21B00E79691AE3EE92C77CE；Setup 229833900 bytes SHA256 789005E023B5326991034C11E934FD62B45754F51254C2726DB626931528F47C；内嵌ZIP限长哈希匹配独立ZIP，包内原生helper匹配编译输出。helper传parent-pid并监测父进程退出，避免强制更新主程序后残留采集进程占用EXE。
- 末次Android主线ARM64 APK：22163351 bytes SHA256 7D97BE66A5270ABDCF6C108FFAD126C49B0952588075D9F32E5FA976EA904623；v2/v3签名、正式证书、16KiB对齐与仅arm64-v8a均检查通过。仍2.2.0+126本地候选，未安装/启动/推送/发布。修改前包在publish/rollback/pre-desktop-video-packaging-20261029。只证明离线构建与本机合成链路，用户睡觉期间没用ADB/手机。

### 刷新后继续的优先事项

1. 先补齐跨端离线验证与代码复查：Windows TLS拒绝/强制重新核对/无确认超时、pin持久加载、取消/并发停止；Android verify dialog在stopped时清理、后台/横竖屏/重连状态。检查DesktopVideoView待处理描述Future取消与重试竞态，避免unhandled timeout。
2. 目前桌面鼠标仍复用旧RemoteInputSender，通过业务TCP与可选旧AES密钥流程，独立TLS媒体并没有自动加密控制指令。必须继续把桌面模式输入接到已认证TLS的上行独立通道，确保不被文件流阻塞、无需复制旧密钥，并复用RemoteInputManager的单一所有权/按键状态释放。旧触控板/游戏模式保持兼容；不能把当前整个远控称为端到端全加密。
3. 补自定义fps/码率/分辨率和实际编码器状态，支持monitor选择/旋转/会话恢复；当前原生rotation显式拒绝、默认monitor0。CPU转换是性能下限，接着实现GPU视频处理/颜色转换再验证；目前只有H264实际路径，不能宣称H265可用。
4. 原生循环父进程监测刚接线、需做不录屏的进程退出测试；无手机下验证GPU/capture初始化可用性时不落盘真实画面。设备真机仍等用户回来，不ADB连接/唤醒/安装。
5. 最终重建主APK/Windows安装包并同步校验值；不要重复仅规划或把当前候选宣称高完成度最终验收。未完成目标时维持heartbeat继续，达到5%停止线保存必要进度，刷新再做。

## 2026-10-29 TLS输入检查点（优先于上面的“刷新后”待办）

已完成上面的优先事项2：桌面模式RemoteInputSender采用desktopTransport，Native方法sendInput -> 128条队列 -> 独立TLS上行JSON input；不再走普通业务连接，不要求旧AES密钥。Windows DesktopMediaServer把已认证peer授权映射到RemoteInputManager独占AuthenticatedControlLease；禁用/断线/退出/过期释放，旧lease不能影响新lease。ready.canControl=false是只读，不会静默回退明文。文本UTF8边界分片<=4000字节。旧触控板/游戏协议不变。NativeReady与ControlReady分开，不把视频已连接误当可操作。

描述请求/页面退出订阅、停止清Texture/核对对话框与迟到确认已加回归。Windows本地拒绝立即结束等待；Android配对阶段并行等待UI选择/服务器结果。现有测试：.NET相关9项+本地拒绝1项，Flutter8项，目标Dart analyze无问题。保存回退包pre-desktop-tls-input-20261029。

下一轮优先做：GPU颜色转换/缩放、显示器/旋转与编码fps/码率参数；配对pin持久加载/强制复核测试；静止桌面下的媒体下行保活与明确断线超时（当前native读取可等待无帧，不能简单超时误断静态屏），自动重连/恢复状态；实际光标形状与源输入位置映射；原生父进程退出不录屏测试。保持真实设备/物理呈现验收待用户回来，不ADB。此任务未完成，不暂停heartbeat、不宣称最终高完成度交付。
- 本阶段最终分发验证：Windows完整Release安装器/ZIP构建成功（不是仅dotnet build）；ZIP 838项完整读取，Hinge.dll、Hinge.Core.dll和原生采集helper与本次发布输出逐项SHA256一致，安装器内嵌载荷长度及哈希和独立ZIP一致。身份MSIX签名有效；Setup外层仍NotSigned。
- Windows Setup 229835430 bytes，SHA256 36E536CC36F5FB16D3B80672A4223B9BE9EA8DE43C27F92F4C3139F585EB1DD8；便携ZIP 161449582 bytes，SHA256 5B573366D6381E7D8BD7826E2C2CD415C35607D07C33F659983762BBC18E700A。
- Android Release主APK 22163351 bytes，SHA256 5EA11786A4DA932BCA5A1AE6CFBE796EF80E84403F02AB86A502ABDDC39A0DAD；仅arm64-v8a、v2/v3签名、正式证书SHA256 3eb0f6708904f8ef916c6a2572e394ba981ee25d64861344cf1921ca7ea17975及16KiB对齐均通过。APK总体字节数与前包相同，但已比对classes.dex及libapp.so内容均变化，不能只凭文件大小判断构建陈旧。
- 当前仍为本地2.2.0候选，未安装或操作真实手机/用户桌面，未提交/推送。更高完成度目标仍未完成；下一阶段优先GPU转换、编码参数/显示器/旋转、静态画面下行保活与断线恢复及相关离线回归。

## 2026-10-29 GPU与静态媒体检查点

GPU转换已实现并实测：D3D11 VideoProcessor缩放/601 NV12转换，持久BGRA输入纹理，NV12读回喂MFT；150次1080p→720p均值0.413ms，三原色与CPU公式一致。不是MFT surface零复制；保留CPU回退。native helper帧率测试300/300、59.98FPS，父进程退出3031ms退出0。实际静态桌面DXGI无首帧已增加Default桌面检查下的单次初始GDI兜底，raw字节仅Stream.Null；实测1920x1080首帧submitted=1 encoded=1，未查看/保存内容，未操作手机。

媒体下行保活/超时已实现：hello/ready协商mediaHeartbeat，HDV1独立flag4、1字节0、每秒无帧时发送，不计视频FPS；native协商后读超时6500ms。每帧写两秒上限、pending iterator取消/Dispose顺序、原生3个in-flight与3秒编码事件超时均已接。pin加载/静态/慢读超时/心跳格式/IPv6与IPv4回归通过。协议同步protocol.md，详情见Hinge.md #400。

下一轮：完善可自定义FPS/码率/清晰度/显示器和rotation（当前仍拒绝旋转屏）；重连恢复策略及错误分类（当前重连为手动按钮，不能自动对身份错误重试）；补完整离线集成与用户醒来后真机验收。不要回退GPU/静态首帧/心跳或误把静态0FPS当掉帧。全程不得ADB/唤醒手机。当前发布重建后验证结果以随后记录为准。
- 安装器进一步处理采集helper的更新占用：只对目标安装目录中精确匹配的Hinge.DesktopVideo.exe等待父进程清理，仍未退出才定向终止；读取路径/退出竞态安全处理。保留StopRunningApp原有“不能杀整个进程树，否则QQ/Weixin等用户应用被误关”的保护，不按仅进程名或目录前缀扩大终止范围。路径白名单7项测试通过；未实际运行安装器或关闭用户应用。
- 最终Windows安装器 229843043 bytes，SHA256 0E99B929C82AAA4997D3BBA4A0EB034BFFC87F3BFEA605BD5F9DBBF16404AB51；ZIP 161456815 bytes/838项，SHA256 E71C218D1507A630FEE72870DEBA4298EA8759E6D84C7ECAB1F4073C92591CCF。ZIP可完整读取、Hinge/Core/Native helper与本次输出一致、内嵌载荷校验匹配、身份MSIX签名有效；Setup外层仍未签名。
- Android主APK 22163351 bytes，SHA256 3AAE6A39AC71D07F7E861FC5176401FF81D9DF989FFDC047E763FBDC8D752D44，仅arm64-v8a；v2/v3签名、正式证书及16KiB对齐通过。Release日志见tmp/desktop-release-windows.log、tmp/desktop-release-android.log。无安装/推送/真实手机操作。
- GPU与媒体保活已具备离线证据，但旋转显示器、自定义画质参数、自动恢复与手机真实显示体验仍未完成；保留heartbeat继续。下一步自动恢复应区分身份拒绝与网络故障，前后台分离，稳定运行后再清重试计数，避免每次ready重置导致永续重连；旧Hinge连接对象更换时不能盲目复用。详细诊断留在本机，不把路径或私密信息塞入未认证0x0084描述响应。

## 2026-10-29 恢复和画质检查点： 桌面远控有限恢复与电脑端画质设置（2026-10-29）

- 本轮开始5小时剩余51%，阶段复查48%，未用重置券；遵守用户睡觉期间禁止ADB/手机连接、唤醒、安装与操作。继续离线开发，未安装Windows候选、未推送发布。
- Android桌面连接新增500/1500/4000ms三次有限重试；稳定ready满10秒才清除次数，短暂ready不清次数，避免采集失败时永续重连。仅已确认身份的网络EOF/Socket/超时或已建立TLS的上行网络故障可重试；身份/配对拒绝、协议/解码失败、输入队列拥塞不自动重试，不自动清pin，也不回退明文。
- Native单次失败标记避免输入writer/保活/读循环同时报错；保活写失败不再静默stop。退出后台取消重试/断流，回前台只恢复之前已经成功观看或待恢复的会话。保留native onStop先于Flutter paused的恢复意图；替换Hinge SessionConnection时解绑旧订阅，取消旧描述与原生会话。普通Hinge会话完全断开仍明确提示，不能盲目复用旧连接对象。
- Windows“设置→手机远控电脑→允许手机查看电脑桌面”原卡片加入帧率15–120、最长边640–3840、码率1–50Mbps滑块，默认60/1920/8；单条LocalSettings JSON保存、加载校验异常回默认，明确按保存应用。共享开启时保存会结束当前观看并重启共享，不在每次拖滑块时重建编码器。状态文案采用实际配置。仍H264优先硬编及自动软件兼容路径，不新增未经验证的H265选项。
- 保存操作禁用共享/停止/重置冲突入口；关闭窗口设置关闭标记，排队的重新启动不能在关闭后开启共享。旧server排队状态/错误回调核对实例，不能覆盖新会话状态。
- 定向回归：Flutter桌面/输入9项通过，覆盖三次上限、短暂ready、身份拒绝、前后台取消/恢复、native先停止的时序；Dart目标analyze无问题。画质配置JSON往返/空JSON默认/上下边界9项.NET测试通过。新UI沿用项目M3正文/Slider，未用有历史模糊问题的Acrylic下拉菜单；WinApp CLI仍不可用，没有冒充GUI点击验收。
- 回退APK位于publish/rollback/pre-desktop-recovery-20261029；Windows原包位于publish/rollback/pre-desktop-quality-settings-20261029，均保留SHA256SUMS。主线ARM64 APK和Windows安装器重新构建，最终产物数据见本节后续记录。
- 未完成：实际显示器选择/旋转支持、真实编码器状态展示、真机画面延迟与鼠标手势验收；当前monitor0、旋转屏仍显式拒绝。下一轮继续这些实现，不能因本次构建通过就宣称整个远控已完整验收。
- 本阶段最终构建与校验：Windows安装器229845779 bytes，SHA256 CA16F770626FD01D633EEDBC315E9BE1950B9A5A571D8FF99B8AD7BE62EFA214；ZIP161459551 bytes/838项，SHA256 B89778F0660F5A2B67574D664A7113D689E1A29D430AB465DE5597C713A4E56E。完整ZIP读取、包内主DLL/Core/native helper与最终构建输出、Setup内嵌ZIP长度/哈希均一致；身份MSIX签名Valid，外层Setup仍NotSigned。最后编译输出时间晚于本轮最终源码修改。
- Android主APK22163351 bytes，SHA256 217B8EC0E040840058B66968784AC08FF98825CACEB0302C90800ABECBD37DE8；仍com.hinge.office 2.2.0+126，仅arm64-v8a、v2/v3签名、原正式证书及16KiB对齐通过。classes.dex/libapp.so都与回退包不同，最新APK时间晚于最终Dart修改。当前候选未安装。
- 最终定向.NET桌面媒体/输入lease/画质14项通过；Flutter桌面/输入9项通过。Release与验证日志：tmp/desktop-quality-release-windows.log、desktop-recovery-release-android.log、desktop-quality-integration-tests.log、desktop-recovery-tests.log；分发证据tmp/desktop-quality-recovery-artifacts.json。仅电脑回环/MockInputInjector，不注入用户实际鼠标键盘，不操作手机。
- 阶段末5小时剩余43%、周剩余60%；仍高于停止线，保持heartbeat继续。下一轮先读本检查点，接实际显示器枚举/选择和旋转转换，随后补实际编码器状态/必要离线测试。不要重复构建已验证同一源码、不要因省略实机限制而宣称已达UU体验。

## 2026-10-29 显示器旋转检查点： 显示器选择与旋转采集（2026-10-29）

- 本轮起始5小时剩余43%，阶段复查37%，未使用重置券。未使用ADB/手机、未安装或发布；仅电脑端只读枚举、合成测试和离线构建。
- 原生helper新增--list-monitors，只枚举DXGI当前AttachedToDesktop输出，不创建采集/编码器。和采集共用枚举顺序，最多16个。Windows设置原共享卡片新增显示器选择和刷新；沿用正文TextBlock和不透明M3下拉背景，避免旧Acrylic文字模糊回归。列表读取3秒截止、8192字符上限，退出/超限只清理自己启动的helper。
- 保存配置新增MonitorDeviceName，采集按Windows逻辑设备名精确匹配，不存在就报错；旧配置无设备名时仍按旧索引兼容。该名称是Windows逻辑显示器标识，并非永久EDID物理身份；更换显卡/扩展坞拓扑后应刷新确认。选项缺失不会自动选第一屏保存，也不静默回退其他屏幕。
- 新增DesktopGeometry的直立画面→采集纹理映射，GPU VideoProcessor处理0/90/180/270度，先检查旋转能力，不支持时保留CPU映射回退。输出尺寸按直立方向计算、校验实际桌面坐标尺寸；鼠标箭头位置按直立桌面尺寸缩放，首帧GDI已经直立，不重复旋转。未修改用户电脑的显示方向或系统设置。
- GPU合成检查三原色及四方向非正方形四象限通过；CPU四方向角点和全部2048像素一一映射/边界检查通过，1080p→720p转换150次均值0.418ms（仅转换，不含网络/手机）。H264合成150帧按PTS59.99FPS、最大18.05ms，系统解码完整150/150。不能以此代替实际竖屏显示器/手机验收。
- .NET显示器列表/配置16项定向测试通过：格式、顺序、旋转尺寸、重复/错号/超长/非法字段拒绝、配置兼容及边界。当前电脑枚举1屏2560x1440；按明确设备名只读采集探测GPU+NVIDIA MFT成功，静态首帧submitted=1 encoded=1，stdout全部丢弃Stream.Null，没有保存或查看真实桌面画面，没有输入注入。
- 本轮修改仅Windows，复用上轮已验证的ARM64主APK，不无意义重建未改的Android。最终Windows完整Release安装器229854441 bytes SHA256 0B236EC70E8708B77ABC1C5DE0537DCDF23579976301AAF9788F74391C68ABF6；ZIP161468213 bytes/838项 SHA256 63463E3F845FB6F0AAC68FE47ACCBAB7A9607C3DD4F03FAEA918537996BB318E。内嵌ZIP长度/哈希、完整ZIP读取、主/Core/native与输出一致；身份MSIX签名Valid，Setup外层仍NotSigned。回退包publish/rollback/pre-desktop-monitors-rotation-20261029。
- ARM64 APK仍22163351 bytes SHA256 217B8EC0E040840058B66968784AC08FF98825CACEB0302C90800ABECBD37DE8。证据tmp/desktop-monitors-rotation-artifacts.json；日志desktop-monitors-release-windows.log、desktop-monitor-tests.log、desktop-rotation-verification.log、desktop-selected-monitor-probe.log。未做WinUI实际点击/手机真机测试。
- 下一阶段重要待办：当前桌面输入仍是系统相对鼠标，可跨显示器；应为桌面会话增加所选屏幕内的指针定位/边界与点击保护，防止观看副屏却操作到画面外。不能直接ClipCursor全局锁住用户鼠标，应仅约束远控事件，并保留独立触控板/游戏原有相对模式。随后完善实际编码器状态显示与必要离线回归；旋转物理屏/真机低延迟体验仍待用户回来。任务未全部完成，heartbeat继续。
- 官方接口依据：https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api 、https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11videocontext-videoprocessorsetstreamrotation 。独立实现映射与合成测试，未并入UU GPL源码。
- 本阶段结束5小时剩余35%、周剩余59%，高于5%停止线；继续保留自动续作，不使用重置券。

## 2026-10-29 鼠标范围检查点： 桌面会话鼠标范围与实际编码器状态（2026-10-29）

- 本轮开始5小时剩余34%；阶段查询有一次工具暂时不可读，随后恢复为剩余26%。未用重置券。仍禁止ADB/手机操作，未安装或推送。
- RemoteInputManager认证lease新增可选独立injector，旧单参数调用及独立触控板/游戏保持原有实现。桌面事件、禁用/断线/过期释放均发送到同一lease的injector；旧lease不能影响新lease。没有全局ClipCursor，也没有限制电脑本人的物理鼠标。
- Win32InputInjector.ForDesktop在认证控制开始时按所选Windows设备名解析HMONITOR，读实际物理桌面边界；每个远程定位事件再核对设备名和边界，拔屏或分辨率/布局变化报错结束会话，抬键/抬鼠标不依赖显示器仍存在。开启共享时也冻结实际逻辑设备名，避免纯索引因枚举变化切错屏。
- 桌面模式的相对触控位移转换为所选屏幕内的绝对指针事件：原光标在屏幕外时从所选屏中心开始，移动按屏幕边界限制；点击/滚动前将定位与操作组成同一SendInput批次，避免定位和点击之间被其他输入插入。虚拟桌面支持负坐标、竖屏、多屏原点与像素中心归一化，避免屏边取整把点击落到相邻显示器。主程序manifest已确认PerMonitorV2。
- 设置页新增实际编码器信息，来自native已初始化encoder=日志（不是用户配置或GPU任务管理器推测），显示硬件/软件、编码尺寸和目标FPS；停止/掉线清空，旧源事件不能覆盖新server状态。保持目标FPS与手机实际视频输出FPS的区别，不宣称物理屏刷新率。
- 17项定向.NET回归通过：鼠标范围/负坐标/极值/逐像素归一化、lease独立注入与释放/旧owner隔离、媒体TLS和既有游戏远控。测试只用Mock，不向用户实际鼠标/键盘注入。平台和主程序Release编译成功，0警告0错误。
- 历史错误补记：直接在PowerShell LoadFrom Hinge.Platform运行只读探测触发Windows App SDK模块初始化异常；非终止错误后续语句曾错误输出passed，已将该日志覆盖为失败说明，不能当验收。改用tmp/desktop-pointer-probe的最小net8控制台链接同一份Win32InputInjector源码、引用Core（不加载WinApp SDK宿主）后，只读解析真实已连接显示器/拒绝不存在设备均通过；未调用任何输入方法。日志tmp/desktop-pointer-readonly-target.log。不要把PowerShell脚本未退出等同测试成功。
- Windows主线完整安装器229858353 bytes SHA256 6CFD04C374D31E8324CB70109AE4960D46E374459ACE6214CF26441330E4C4BC；ZIP161472125 bytes/838项 SHA256 42A0E63209538E6B56C6BE4C0187D97563C575603E25E9AFDED1F0889AC1A32B。完整ZIP读取、Hinge/Core/Platform/native逐项匹配最终输出、Setup内嵌载荷长度/哈希一致，身份MSIX签名Valid，外层Setup仍NotSigned。最后输出晚于最后源修改。回退包publish/rollback/pre-desktop-pointer-scope-20261029。
- 本轮Windows-only，Android仍使用上轮已验证ARM64主APK22163351 bytes SHA256 217B8EC0E040840058B66968784AC08FF98825CACEB0302C90800ABECBD37DE8。分发证据tmp/desktop-pointer-artifacts.json；Release和测试日志desktop-pointer-release-windows.log、desktop-pointer-integration-tests.log。
- 未安装或点击新UI、未真实注入鼠标，物理多屏/旋转屏/手机手感仍未验收。下轮优先复查Android低延迟呈现与输入生命周期、必要统计/失败有界处理，准备明确可执行真机验收项；当前光标仍简化箭头，游戏模式尚未叠加到桌面视频视图，不得把这些描述为已完成。真实设备验收等用户醒来，不以假定失败要求用户提供USB。任务继续，未达到全部完成暂停条件。
- Win32接口依据：https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-mouseinput 、https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput 。没有更改UIPI、权限、安全桌面或系统安全策略；普通权限能否操作目标应用仍受Windows权限约束。
- 本阶段结束5小时剩余22%、周剩余57%，仍高于停止线；自动续作保持开启，继续离线实现，达到<=5%再保存并等待刷新。

## 2026-10-29 Android进度检查点： Android解码阻塞、输出统计与迟到输入回调（2026-10-29）

- 本轮起始5小时剩余21%，阶段复查16%、14%、12%；未用重置券，不使用ADB、不连接/唤醒/安装/操作手机。仅离线构建与模拟回归。
- 修复TouchpadScreen桌面输入Future的迟到回调：拒绝/异常只影响发起时相同sessionId，旧连接的错误不能结束已经重连的新控制。新增两项Widget回归分别覆盖迟到false和PlatformException，均通过。测试需等待ready引发的父子布局更新完成再点按钮；初次少等一次frame导致没有发送事件，不记录为产品输入故障。
- MediaCodec解码输入连续等待超过250ms主动终止本次会话，存在待解码帧且连续1秒取不到输出才报错；静态桌面pending=0不超时，已经有可用输出时先处理输出不误判。失败由native现有单次fail路径关闭socket并释放Windows控制，不无限积压旧画面或自动无限重连。这两个阈值是当前实现的保护值，仍需真机校准，不等于实测时延保证。
- 新增不依赖Android API的DesktopDecodeState，实际解码器使用其进度计数/截止与裁剪范围检查。Release编译后的实际Kotlin类用JDK17和已缓存Kotlin标准库执行src/test/java/.../DesktopDecodeStateCheck.java（java -ea），验证输入250ms边界、输出1000ms边界、空闲/重置不误断、pending计数与裁剪边界。可重跑scripts/test_desktop_decode_state.ps1，不新增JUnit/Robolectric或模拟手机。
- 输出尺寸遵循MediaCodec crop-left/right/top/bottom的有效区域，处理1088存储高度/1080可见高度等对齐情况，避免把编码器padding当成画面比例；缺少成对crop字段回原尺寸，非法范围拒绝。纯JVM检查通过；实际厂商解码器/Surface行为仍未验收。接口依据：https://developer.android.com/reference/android/media/MediaCodec （Output Format/crop rectangle）。
- native统计补充实际decoder.name、API29+硬件/软件属性、待解码帧数，每秒随已有stats上报，不逐帧穿Dart；Dart输出徽标显示实际解码器和待解码数，宽度限在屏内、最多三行。视频输出FPS仍来自OnFrameRendered，不是屏幕物理刷新率，也不是编码帧数。旧stats没有新字段仍兼容显示。
- 14项Flutter桌面/输入/触控板回归通过；目标Dart analyze无问题。Android主线ARM64 Release构建成功，原生Kotlin通过编译；纯JVM进度/几何检查通过。没有MediaCodec真机单元测试，不把上述纯状态测试当成视频硬件验收。
- 最终主APK22163351 bytes SHA256 D02E88D3ED6903C21965D1E5EB5616B0B4F312FAC8AB8212E9BDF48BA34CD080；正式证书SHA256 3eb0f6708904f8ef916c6a2572e394ba981ee25d64861344cf1921ca7ea17975，v2/v3签名、仅arm64-v8a、16KiB对齐与ZIP完整读取通过。APK时间晚于最终生产源码。前包位于publish/rollback/pre-desktop-android-progress-20261029。
- Windows源码本轮未改，保留已验证最新安装器229858353 bytes SHA256 6CFD04C374D31E8324CB70109AE4960D46E374459ACE6214CF26441330E4C4BC、ZIP161472125 bytes SHA256 42A0E63209538E6B56C6BE4C0187D97563C575603E25E9AFDED1F0889AC1A32B；复核内嵌载荷和Hinge/Core/Platform/native输出一致。证据tmp/desktop-android-progress-artifacts.json，日志desktop-android-progress-release.log、desktop-android-stall-tests.log、desktop-decode-state-check.log、desktop-android-progress-signature.log、desktop-android-progress-alignment.log。
- 下一轮：检查首帧光标显示和目前简化箭头、桌面视频与游戏覆盖的适配范围，以及准备实机验收。原生首帧GDI路径目前不画箭头，鼠标移动后的DXGI帧才显示；应复用同一绘制函数补齐。真实旋转屏/手机延迟、权限提示、触控/缩放/恢复等仍待用户回来，不擅自使用手机。达到5%停止线保存进度等待刷新，未完成不暂停自动化。
- 本阶段结束5小时剩余10%、周剩余55%；高于停止线，下一轮只选择可在剩余额度内完整验证的小项，任何阶段<=5%立即保存检查点停止开发，刷新后续作。

## 2026-10-29 首帧光标检查点： 首帧鼠标标记与边界绘制（2026-10-29）

- 本轮起始5小时剩余9%，阶段复查7%，未用重置券、不操作手机/ADB。选择小项完成，未扩张到新的大功能。
- 将原有NV12简化箭头绘制集中到DesktopGeometry.DrawDesktopPointer，初始GDI快照与后续DXGI帧共用DrawCursor，避免首次静止桌面无光标、必须移动后才出现。只在系统光标显示且实际位于所选屏幕内绘制，避免相邻屏光标的箭头尾部误出现在当前画面。
- NV12画布尺寸、偶数宽高、最大尺寸和完整缓冲区大小检查；屏外极值提前返回，屏边裁剪防止越界。新增--verify-pointer仅操作合成缓冲区，不读取桌面/系统光标；2x2、12x12、128x128的尖端、右下边缘、屏外极值、非法画布拒绝检查通过。构建日志tmp/desktop-pointer-firstframe-build.log，自检tmp/desktop-pointer-marker-check.log。
- 仍为简化箭头，未实现系统I-beam/手形/自定义光标形状，不能描述为原生全光标还原。未录制或查看真实桌面，未注入鼠标按键，也未安装候选。
- 前Windows包保存在publish/rollback/pre-desktop-firstframe-cursor-20261029（SHA256SUMS.txt）。Windows完整Release构建正在进行，日志tmp/desktop-firstframe-release-windows.log；完成后校验载荷并记录新哈希。Android本轮无改动，保留上轮ARM64主APK D02E88D3ED6903C21965D1E5EB5616B0B4F312FAC8AB8212E9BDF48BA34CD080。
- 下一轮先检查额度，<=5%仅保存并等待刷新，不能继续开发或消耗重置券。刷新后继续桌面视频/游戏模式适配范围、实际光标形状及明确实机验收准备。用户未醒来前手机仍禁止使用，任务未全验收不暂停自动化。
- 本阶段最终Windows安装器229859530 bytes SHA256 E291730FC5945A79B146D39C786A4A9307B8D3D1AD29B9D7C45332E1D35E4E00；ZIP161473302 bytes/838项 SHA256 DDD8C369BEE7CD12B16AB97DB7E0D6358CA590FA514432EF1B205FC2EDBFBAAF。完整ZIP读取、Hinge/Core/Platform/native匹配、Setup内嵌载荷一致；身份MSIX Valid，Setup外层NotSigned。包内helper再次执行--verify-pointer通过，文件时间晚于最后生产源码。证据tmp/desktop-firstframe-artifacts.json。
- 新增docs/DESKTOP_REMOTE_ACCEPTANCE.md，列两端设置入口、首次核对、鼠标/手势/缩放、屏幕选择、连续运动和恢复停止的真机预期，明确H264桌面方向与原手机投屏编码选项不同、真实FPS/延迟尚未验收。不是要求夜间操作手机。
- 阶段末查询5小时剩余6%、周剩余55%；不再新增大项，下一轮继续先查停止线。任务尚未完成，保持自动续作，不用重置券。
- 停止线检查：5小时已用95%、剩余5%，已停止开发；当前源码/包/测试和待办均保存，未留下构建进程。预计本窗口刷新 2026-10-29 06:26:32 +08:00。保持heartbeat仅检查额度，刷新后从本检查点继续，不使用重置券，不操作手机。

## 2026-10-29 游戏覆盖检查点： 桌面视频游戏覆盖与相对鼠标协商（2026-10-29）

- 5小时额度已刷新，本轮起始剩余100%、周剩余53%；阶段检查剩余84%。未使用重置券，继续遵守禁止ADB/手机连接、唤醒、安装、操作；仅离线实现与模拟/电脑端构建。
- Android电脑桌面页横竖屏均提供游戏模式入口。透明GameModeScreen叠在同一DesktopVideoView/Texture上，进入/退出不新建Native会话；复用RemoteInputSender的同一TLS控制通道、原预设管理、方向轮盘、多指按键/双指拖拽、触控反馈、图片/文字布局编辑及电脑同步。普通触控层在游戏下方IgnorePointer，避免同一次操作同时触发两套手势。游戏时隐藏视频统计徽标，提示文字增加白色阴影以适应视频背景。
- GameModeScreen支持嵌入退出回调，退出按钮/系统返回仅关闭覆盖；编辑中返回保留原放弃确认逻辑。退出前释放键鼠；能力丢失、控制失效、后台切换仍释放。恢复用户进入前的手动横/竖屏设置，修正原先手动竖屏进入游戏退出后总恢复横屏的分支。
- 游戏视角不能套用普通桌面绝对指针/屏边限制，否则会影响游戏鼠标捕获。新增MouseMove专用RemoteInputEvent.Flags 0x0004相对位移请求；认证lease仅在该动作/标记下调用MoveRelativeMouse，其余移动继续受所选屏范围限制。Win32只检查显示器身份/布局仍有效，不ClipCursor、不更改系统安全策略。点击和释放规则保留；旧独立触控板/游戏仍相对移动，兼容原通道。
- TLS ready新增relativeMouse能力位；新Android仅收到明确true且允许控制才启用桌面游戏入口，缺失/false不会把视角命令错误发给只会绝对定位的旧电脑端。能力丢失时已按键释放并禁用控件，无明文回退。协议protocol.md已同步；注意输入0x0004和HDV1媒体heartbeat 0x04不是同一字段。
- 定向验证：Flutter桌面/输入/触控板/游戏与预设20项通过，Dart目标analyze无问题；.NET认证lease/媒体TLS/原游戏/鼠标几何17项通过；JVM解码截止/裁剪自检继续通过。新增Widget检查同一Texture、只有一次native start且覆盖退出前零stop、相对鼠标标记、按键按住退出与能力丢失释放、系统返回不退出桌面；发送器检查未协商不发送相对请求，普通移动flags仍0。测试全部Mock，未向用户真实桌面注入输入。
- Windows主线完整安装器229859929 bytes SHA256 DA9B2A6B1CE26D876CF3EC61A86139BE901A0BCF910F7A479726693FF4299AA5；ZIP161473701 bytes/838项 SHA256 96C1B1CA0831E2A000C245630FA45739EBA4C4CF12E937653C2FA157ADB2E7D2。完整ZIP读取、Hinge/Core/Platform/native一致、Setup内嵌载荷长度/哈希通过；身份MSIX Valid，Setup外层仍NotSigned。
- 主APK22163351 bytes SHA256 853C084E32029E54B12621A86A05FB659C01A061868526E28ED63DF2B092F89D，原正式证书、v2/v3、仅arm64-v8a、16KiB对齐均通过。仍2.2.0+126/Windows2.2.0.0本地候选。三件前包在publish/rollback/pre-desktop-game-overlay-20261029，未安装/提交/推送/发布。
- 证据tmp/desktop-game-artifacts.json，日志desktop-game-release-windows.log、desktop-game-release-android.log、desktop-game-all-tests.log、desktop-game-input-tests.log、desktop-game-overlay-tests.log、desktop-game-decode-check.log。验收清单docs/DESKTOP_REMOTE_ACCEPTANCE.md已更新游戏步骤与真实兼容性边界。
- 下一步优先实际光标形状（当前仍简化箭头）及离线代码审查/验收准备。视频游戏覆盖离线接线已完成，不等于实际游戏/厂商解码/无线低延迟已验收；不得绕过Windows UIPI、游戏反作弊或安全策略。手机仍须等用户明确可用后才测试；未全验收保持heartbeat继续。
- 本阶段结束5小时剩余80%、周剩余50%；额度充足，自动续作继续，不使用重置券。下一轮从本检查点继续，保留所有原有改动/回退包，不重复构建相同源码或擅自发布。

## 2026-10-29 系统光标检查点： 系统光标形状、热点与小区域合成（2026-10-29）

- 额度工具前两次暂不可读，第三次恢复；起始有效值5小时剩余78%，阶段末72%、周剩余49%。不使用重置券，不连接/操作手机，也没有录制/查看真实桌面或注入输入。
- Windows原生新增DesktopCursor，GetIconInfo读取当前系统光标尺寸/热点，DrawIconEx在光标尺寸的离屏DIB中处理32bit透明与单色AND/XOR图像，合成回NV12。文本I-beam/手形/缩放/忙碌/交叉等形状不再统一画简化箭头；首次GDI与后续DXGI都共用此路径，失败或超出512像素形状上限回退原箭头。
- 只转换光标小区域的NV12↔RGB，透明像素对原画面逐字节保持；色度按2x2更新。复用DC/DIB和暂存数组，GetIconInfo位图及时DeleteObject，借用系统HCURSOR不销毁；位置按真实热点及视频缩放映射，屏边和极值提前裁剪。原生每帧不增加整屏BGRA回读。
- DXGI帧按独立指针可见性元数据决定是否额外绘制；已有嵌入光标的图像不再次覆盖，位置更新为0时沿用上次元数据。GDI静态首帧仍单独绘制。实际多驱动/旋转屏/不同DPI仍待真机验证，不能把内置资源测试当作这些硬件已验收。
- shortcut：DrawIconEx动画步长当前为0，动画光标只显示首帧；没有实现完整动画时序。自定义超大/读取失败的光标走回退，不能宣传所有自定义光标完全还原。
- --verify-system-cursor只使用系统内置光标资源和合成缓冲区：6种形状、左上/右下裁剪、完全透明背景逐字节保持、自建单像素光标热点、屏外极值与150次GDI资源不增长检查通过。单次小区域合成均值0.013ms，打包后复测0.019ms（不含采集/编码/网络/手机）。原简化箭头边界检查继续通过；GPU颜色/四方向旋转检查继续通过，1080p→720p转换均值0.406ms。
- 修正自检std::fill字面量引发的C4244警告后，Windows完整Release构建成功；包内helper再次--verify-system-cursor通过。未安装候选或现场点击新UI；本轮Android未改，保留上一主线ARM64 APK。
- 最终Windows安装器229864064 bytes SHA256 DB05587B6719309424AFCAE890286813AEF48E6C7773870CA8941F802F7763DA；ZIP161477836 bytes/838项 SHA256 9BA6D1CFFF9B109969D162C40BCBEB4ED4B8F17BCDAE0B416754DEA72193C309。完整ZIP读取、Hinge/Core/Platform/native匹配、Setup内嵌载荷一致；身份MSIX Valid，Setup外层NotSigned。前包publish/rollback/pre-desktop-system-cursor-20261029。
- Android主APK仍22163351 bytes SHA256 853C084E32029E54B12621A86A05FB659C01A061868526E28ED63DF2B092F89D。证据tmp/desktop-system-cursor-artifacts.json；日志desktop-system-cursor-release-windows.log、desktop-system-cursor-check.log、desktop-system-cursor-gpu-check.log。真机验收清单已同步光标形状/热点/双光标检查。
- 接口依据：https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-drawiconex 、https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-iconinfo 、https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api 。没有复制UU GPL代码，没有改变安全策略或系统光标设置。
- 下一阶段：复查真实帧呈现与低延迟统计边界，必要时区分native视频输出与Flutter应用绘制节拍，避免又用接收/解码数字当肉眼实际帧率；继续离线代码审查和验收准备。当前手机仍禁止使用，不能声称已达UU真实效果；保持自动续作，<=5%保存停止，用户明确可用后再做真机验收。

## 2026-10-29 纹理取帧检查点： 真实纹理取帧统计与界面计时边界（2026-10-29）

- 本轮开始5小时剩余70%，阶段末46%、周剩余45%；未使用重置券，未连接/唤醒/安装/操作手机，继续离线实现和测试。
- 关键复核：当前Flutter引擎5d531788691ec3404cac0cee66ead4007b177363的纹理更新调用ScheduleFrame(false)，走DrawLastLayerTrees；该路径没有普通DoDraw的OnFrameRasterized通知。不能把SchedulerBinding FrameTiming计数当成全部纹理绘制FPS，更不能把它的低值直接判成视频卡顿。初始实现已在本轮交付前纠正，去掉应用绘制FPS/应用最大间隔；保留的FrameTiming只统计“界面重建P95”。
- 使用当前SDK的SurfaceTextureEntry.setOnFrameConsumedListener，在引擎updateTexImage之后读取SurfaceTexture.timestamp，按实际新图像时间戳去重，统计“纹理取帧FPS”和最大取帧间隔。不会把同一图像的多次UI重绘算成新视频帧；不替换引擎自己的OnFrameAvailableListener。回调只读取时间戳和更新原子计数，每秒通过已有stats上报，不传视频像素到Dart，也不强制刷新界面凑FPS。时间戳读取失败仅将该项标为待采样，不终止视频；结束时解绑listener。
- 同时记录MediaCodec OnFrameRendered实际nanoTime的最大视频输出间隔，重复/倒序/无效时间不推进基准，重建解码器重置；旧decoder的迟到回调按实例身份忽略。增加系统报告的当前display Hz，仅查询，不强制更改显示模式。所有这些都是管线/模式统计，仍不能宣称物理scanout或真实端到端延迟。
- DesktopFrameStatistics为有界512样本/约1秒窗口的UI重建P95，按engine时间戳处理批量回调，超过3秒无新样本标为待采样，重连/关闭清空；add/removeTimingsCallback不覆盖其他观察者，游戏覆盖保留原隐藏HUD行为。HUD最多4行，优先显示视频输出与纹理取帧、各自间隔、屏幕Hz、UI重建P95、解码器和待解码数；丢弃先前不直观的累计收到/解码总数显示（native数据仍保留）。
- 顺带修复会话替换生命周期：旧Session即使已经不是active，清理时仍按自己的sessionId通知stopped，使保留的旧页面退出ready/清空Texture；只有active身份一致才清除全局active，不能结束新会话。旧错误/统计仍按active过滤。新增双视图模拟验证旧stop只清旧页面，新页面继续ready。
- 定向验证：Flutter统计/桌面/输入/触控板/游戏25项全部通过，目标Dart analyze无问题。JVM自检扩展实际编译的DesktopDecodeState/DesktopTextureStatistics，验证视频间隔的重复/倒序/重置、新图像去重、重复重绘不计数、无效时间戳与恢复、最大间隔原子读取，原解码截止/裁剪检查继续通过。没有MediaCodec/Texture真机验收；测试的“视频60/纹理45”仅为模拟数据，不能拿来宣称实际测得。
- ARM64主线Release成功，最终APK22228887 bytes SHA256 1A5D9C609833DBF3972A8A6FBBF616AEDFE5177752295473944A41C89251ACF3；正式证书、v2/v3签名、仅arm64-v8a、16KiB对齐、ZIP完整读取通过，APK时间晚于最后生产源码。前包publish/rollback/pre-desktop-frame-cadence-20261029。
- Windows本轮未改，保留安装器229864064 bytes SHA256 DB05587B6719309424AFCAE890286813AEF48E6C7773870CA8941F802F7763DA，ZIP161477836 bytes/838项 SHA256 9BA6D1CFFF9B109969D162C40BCBEB4ED4B8F17BCDAE0B416754DEA72193C309；载荷/输出重新比对通过。证据tmp/desktop-cadence-artifacts.json，日志desktop-frame-cadence-release-android.log、desktop-cadence-all-tests.log、desktop-frame-cadence-analyze.log、desktop-cadence-jvm-check.log、desktop-cadence-apk-signature.log、desktop-cadence-apk-alignment.log。
- 重要源证据：D:\flutter_sdk\engine\src\flutter\shell\common\shell.cc OnPlatformViewMarkTextureFrameAvailable；rasterizer.cc DrawLastLayerTrees/DoDraw；Android SurfaceTextureWrapper.java updateTexImage、FlutterRenderer.java SurfaceTextureRegistryEntry、TextureRegistry.java OnFrameConsumedListener。对应不可变源码 https://github.com/flutter/flutter/tree/5d531788691ec3404cac0cee66ead4007b177363/engine/src/flutter 。不要以后再次用FrameTiming漏掉纹理快速重绘，或给SurfaceTexture另设OnFrameAvailableListener把引擎回调覆盖。
- 下一轮先做整体离线生命周期/安全边界审查和必要回归；不要为消耗额度随意增加设置或在没有实机数据时强行修改系统刷新率。真正的无线端到端延迟、厂商纹理回调准确性、物理多屏/游戏兼容性仍待用户明确手机可用后验收。保持自动续作，<=5%保存停止，不发布/推送。

# 409. 会话重启竞态与首帧截止（2026-10-29）

- 本轮先查5小时额度，有效值从剩余44%降至33%，周剩余43%；中间一次查询失败后已恢复，不使用重置券。持续遵守不连接/唤醒/安装/操作手机，不注入实际输入，不推送发布或改安全策略。
- Dart停止状态在等待native stop前立即清除。所有重启入口（设备替换、手动重连、网络恢复）共用带generation校验的_restart；旧停止Future完成后不能关闭新ready或再启动多余连接。新增延迟stop/快速三次设备替换Widget回归。首帧等待期间进入后台也保留恢复意图，包括native onStop先于Flutter生命周期通知。
- Windows NativeDesktopVideoSource对从采集进程读到真实视频access unit设置8秒总截止；配置包/heartbeat不能延长截止。首帧后撤销启动超时，静态桌面可以合法无新图像；用户取消仍是取消，不误报启动失败。直接取消正在进行的Stream.ReadAsync，无WaitAsync遗留后台读取。超时沿用finally回收自有helper及诊断读取。InternalsVisibleTo仅为定向测试，不暴露生产设置。
- Android从TLS ready改为connecting提示，须已有解码视频输出并收到Flutter纹理消费回调后才发送UI ready/控制能力；不依赖厂商OnFrameRendered是否及时回调。8秒首帧截止在main Handler独立执行，不能被MediaCodec初始化或媒体读取线程阻塞；失败关闭网络/控制且不自动反复创建卡住的codec。停止时移除截止；已停止会话的排队connecting/ready/format/stats按身份和stopped过滤，error仍可投递。
- 边界：Android系统MediaCodec若自身阻塞，应用不能强制中断驱动调用；当前可及时报错、断开并释放服务器输入，native清理由阻塞返回后继续。此处未宣称能强行回收卡死驱动或已验收厂商兼容性。纹理消费不是屏幕物理scanout。
- 验证：Windows桌面/认证输入相关54项通过，其中新增6项首帧无数据、配置/保活不算视频、关键帧合并配置有效、静态桌面取消、用户取消区分。Flutter桌面/帧统计/输入/触控板/游戏28项通过，Dart目标analyze无问题；已有JVM解码截止/裁剪/纹理统计检查通过。测试均为模拟流/Mock，未碰手机或实际输入。
- 构建教训：第一轮构建期间小改源码，class时间虽然晚于编辑，但javap首帧回调仍是旧getSubmittedFrames；不能用mtime单独证明包含最后修改。已冻结源码重新构建，javap确认回调getDecodedFrames，最终主线APK须以本节后续核验值为准。不要交付构建中途变更之前的包。
- Windows主线安装器已成功，229865511 bytes SHA256 E4C6B59EEBA660645FC00B480BF9D2980CFDC3C8A7B962BAE9CE4F9BB27A673B；ZIP161479283 bytes/838项 SHA256 4D3764315065F4990FB360810B608B89F8428E13C9D5A218B097BB92A6E8624F。载荷/生产DLL/helper一致性通过，身份MSIX Valid，外层Setup仍NotSigned。前包publish/rollback/pre-desktop-startup-lifecycle-20261029已保留三件。
- Android冻结源码主线Release成功，最终APK22228887 bytes SHA256 7D84FF712EE9E56972916997B1ABE35BA8BDCD0496DC7E464669D41157AB50DB。正式证书SHA256 3eb0f6708904f8ef916c6a2572e394ba981ee25d64861344cf1921ca7ea17975、v2/v3、仅arm64-v8a、16KiB对齐、ZIP完整读取均通过。版本仍2.2.0+126/Windows2.2.0.0；候选未安装。
- 最终证据tmp/desktop-startup-artifacts.json，日志desktop-startup-regression.log（Windows54项）、desktop-startup-all-tests.log（Flutter28项）、desktop-startup-analyze.log、desktop-startup-jvm-check.log、desktop-startup-session-bytecode.log、desktop-startup-release-windows.log、desktop-startup-release-android-frozen.log、desktop-startup-apk-signature.log、desktop-startup-apk-alignment.log。Setup内嵌载荷与ZIP一致，相关DLL/helper和打包输出一致。
- 下一轮从此处继续；本轮离线生命周期问题已修好，不要重复构建相同源码，也不要为了额度消耗无依据调整码率/系统刷新率或另加选项。先确认是否仍有可通过离线证据解决的具体风险，否则保留候选等待用户明确手机可用后进行docs/DESKTOP_REMOTE_ACCEPTANCE.md中的真机验收。无线端到端延迟、厂商纹理/解码、实际游戏和物理多屏仍未验收，禁止宣称已经达到UU。自动化维持ACTIVE，完整验收前不暂停；额度<=5%时仅保存停止。

# 410. 高帧率合成验证与拥塞/输入边界复查（2026-10-29）

- 心跳起始5小时剩余31%、周剩余43%，不使用重置券。先读#409最新检查点与历史错误；未连接/唤醒/安装/操作手机，未采集真实桌面、发送真实输入或更改系统安全策略。
- 离线复查DesktopMediaServer的64KiB发送缓冲/每包2秒写截止、单viewer互斥、reader保活5秒、源迭代器取消清理及AuthenticatedControlLease所有权/3秒释放；Android原生输入128队列、socket停止、Dart sessionId隔离及触控板/游戏指针取消、后台释放路径。没有发现本轮有证据支持的新问题，未为消耗额度另改生产代码或参数。
- 使用当前安装包对应tmp/Hinge-win-bundle/Hinge.DesktopVideo.exe，运行--test-pattern --fps 120 --bitrate 8000000 --duration-ms 5000，只有自行生成的1280x720移动亮条/亮度画面，二进制stdout直接流写临时文件。NVIDIA H.264 Encoder MFT hardware=1实际选中，submitted=600、encoded=600；Annex B/SPS/PPS/IDR、单调无B帧PTS及有界协议检查通过。PTS节拍119.98 FPS，最大输入PTS间隔9.76ms；Media Foundation读文件完整解码600/600。
- 严格边界：这只是5秒720p低复杂度合成编码与完整解码测试，未覆盖1920长边/真实桌面采集转换/复杂游戏、无线传输、Android MediaCodec/纹理消费或物理显示。不能写成手机实测120 FPS、实际低延迟数值或UU体验验收，也不能因电脑编码通过就排除手机/网络/显示瓶颈。当前输入PTS节拍不是编码耗时或端到端延迟。
- 两个候选硬件MFT拒绝输出类型(HRESULT 0xC00D6D76)后按既有枚举选到NVIDIA；最终成功，不是整个编码失败，不能仅凭前两行stderr误判。没有增设编码器或换编码协议。
- 新证据tmp/desktop-synthetic-120-evidence.json、desktop-synthetic-120-encoder.log、desktop-synthetic-120-cadence.log、desktop-synthetic-120-decoded.log（仅合成数据HDV1在tmp）。源码/安装包未改，因此不重复编译相同APK/安装器；最终交付和回退仍以#409为准，未推送发布。
- 当前离线必要实现和定向回归已就绪，后续心跳如无可验证的新问题，应安静保留候选，不重复同一测试或空转打包。真实验收须用户明确手机可用后按docs/DESKTOP_REMOTE_ACCEPTANCE.md执行；保持自动化ACTIVE，不因离线通过擅自标完整完成。

# 411. 用户实测两端EOF：Android Keystore TLS签名授权修复（2026-10-29）

- 用户已自行测试，反馈手机/电脑两端均报错，电脑显示Received an unexpected EOF or 0 bytes from the transport stream；本轮没有通过ADB或其他方式操作手机。现运行D:\Hinge\Hinge.exe；现有lifecycle.log未记录桌面错误阶段，不能仅凭EOF确认具体手机端异常。
- 查到确定的源代码缺陷：DesktopClientIdentity v1密钥仅授权SHA256/SHA512，缺少DIGEST_NONE。Android官方setDigests明确说明TLS通常在栈内先计算摘要，须授权NONE；Conscrypt CryptoUpcalls对EC不可导出私钥使用NONEwithECDSA。该缺陷会导致Keystore拒绝原始签名、握手提前断开，符合用户EOF现象。属于已证实代码缺陷/高度相关推断，尚非取得用户手机堆栈后的根因闭环。
- Android新身份授权NONE/SHA256/SHA384/SHA512；增加签名预检：用实际私钥NONEwithECDSA签名32字节摘要、实际公钥验签，不能签名则在连接前明确报错。兼容的v1身份继续使用；不兼容v1保留原条目，生成v2而非覆盖原私钥。已有v2始终优先，不因v1后续可用而切回旧身份。签名只在建会话时执行，非每帧；私钥仍AndroidKeyStore不可导出。
- 不自动清除手机server pins或Windows client pins，不放宽证书校验/TLS版本，不回退明文。此前若确有成功保存的旧客户端pin，v2会触发身份变化拒绝，需要用户通过电脑“设置→手机远控电脑→清除手机桌面身份”显式重置后核对；未成功配对的首次用户无需此步骤。保留旧身份和原pin用于回退，不自动删Keystore数据。
- 两端错误新增阶段前缀（初始化身份、连接服务、TLS安全握手、核对身份、桌面视频传输/接收），Windows错误记录到现有%LOCALAPPDATA%\Hinge\lifecycle.log desktop-connection-error，换行清理，不记录私钥/控制密钥/视频。用户下一次失败可区分握手、确认、采集，避免只暴露泛化EOF。
- 新增Windows握手前断开/释放viewer/下一客户端正常ready回归，相关桌面/认证输入55项通过；补强等待释放通知避免测试竞态。JVM实际Release类原始ECDSA签名、错误公钥/RSA私钥拒绝，以及既有解码/裁剪/纹理统计自检通过。没有真AndroidKeyStore/Conscrypt跨端实测；纯.NET Schannel回环与JVM软件密钥通过不能证明Android TLS兼容，之前遗漏正是此边界，后续不要再次把离线测试当成手机配对已验收。
- Android最终冻结源码Release成功，APK22228887 bytes SHA256 71157947FBBFD21308A7B01D332E8DB2A5F7D2302C835B43DEF5142BBE8C9C60；正式证书3eb0f6708904f8ef916c6a2572e394ba981ee25d64861344cf1921ca7ea17975、v2/v3、仅arm64-v8a、16KiB对齐及ZIP完整读取通过。javap实际Release身份类包含v2、NONE、SHA384和canSign预检。
- Windows完整安装器229865613 bytes SHA256 CB5557A96357A30EC0B56F6A45D124FF3E0740CB499AB83F89ABC4225F02498F；ZIP161479385 bytes/838项 SHA256 886D9779ADC41E91C32C41702430CC6220D9296B3E2CD44AA9C198339450EB16；内嵌载荷、生产DLL/helper与打包输出匹配，身份MSIX Valid，外层Setup仍NotSigned。回退包publish/rollback/pre-desktop-tls-signing-20261029。
- 证据tmp/desktop-tls-signing-artifacts.json；日志desktop-tls-signing-dotnet-tests.log（55项）、desktop-tls-signing-eof-test.log（释放通知补强后单项通过）、desktop-tls-signing-jvm-check.log、desktop-tls-signing-identity-bytecode.log、desktop-tls-signing-release-windows.log、desktop-tls-signing-release-android-final.log、desktop-tls-signing-apk-signature.log、desktop-tls-signing-apk-alignment.log。保持2.2.0+126/Windows2.2.0.0本地候选，未安装/推送/发布。
- 本轮额度起始5小时剩余28%，末次剩余23%、周剩余41%，不使用重置券；下一步请用户覆盖更新手机APK（电脑包包含分阶段日志），自行发起连接验证。不要未经明确授权操作手机；若仍失败先读新版Windows lifecycle.log和手机分阶段错误，不能继续无证据改TLS设置。自动化仍ACTIVE，<=5%保存停止。
- 官方依据：https://developer.android.com/reference/android/security/keystore/KeyGenParameterSpec.Builder#setDigests(java.lang.String...) ； https://android.googlesource.com/platform/external/conscrypt/+/c061be0/src/main/java/org/conscrypt/CryptoUpcalls.java 。没有复制其实现代码，只使用标准JCA/Android API。

# 412. 用户要求继续EOF修复：旧版客户端仍在运行（2026-10-29）

- 再次查询额度，5小时已用81%/剩余19%，周已用59%；不使用重置券。先复读#411和两份实施文档。用户要求继续修复，但此前同一聊天约束仍不在夜间通过ADB/连接/唤醒/安装/操作手机；本轮未操作用户设备。
- 本机D:\Hinge\Hinge.exe PID 5260的进程启动时间为09:03:10。本轮修复后Windows安装器生成时间09:17:51，手机APK生成时间09:19:24，所以正在运行的Windows进程明确早于修复后的电脑候选。现有lifecycle.log最后一条09:15:24，仅是托盘隐藏，没有desktop-connection-error；因此当前电脑进程既不能验证新阶段错误记录，也不能代表新候选。
- 修复后的包已核对：Windows Setup SHA256 CB5557A96357A30EC0B56F6A45D124FF3E0740CB499AB83F89ABC4225F02498F；Android ARM64主包SHA256 71157947FBBFD21308A7B01D332E8DB2A5F7D2302C835B43DEF5142BBE8C9C60。包签名、ABI/对齐及Windows载荷匹配已在#411验证。本轮未改源码，因此不重复构建或复跑相同测试。
- 当前离线证据不足以确认手机TLS握手已通过。最直接的验证是用户自行更新并重新启动电脑Hinge、覆盖安装手机APK后再次连接；此操作不会清除设置。若仍错，先采集两端新阶段错误及电脑lifecycle.log再针对实际失败阶段修复。未要求重新启用已暂停的heartbeat安排。

# 413. 手机电脑桌面沉浸全屏视图（2026-10-29）

- 将“全屏显示桌面”加入手机桌面页横屏工具栏和竖屏应用栏。进入后锁定横屏、隐藏系统栏，视频铺满主体区域，右侧保留窄黑色操作栏：操作、键盘、显示桌面、所有窗口、退出全屏。操作菜单复用既有左右键、画面缩放和游戏模式；“显示桌面”“所有窗口”分别发送 Win+D 和 Win+Tab。
- 全屏与普通桌面共用同一 DesktopVideoView/Texture 和 TLS 会话，不重建视频；全屏隐藏性能徽标，鼠标/双指滚动/拖拽仍落在同一画面触控层。进入前保存应用内方向设置，退出按钮或系统返回恢复方向与系统栏。全屏中进入游戏模式后退出会重新隐藏系统栏并恢复全屏。
- 静态检查：`flutter analyze lib/app/touchpad_screen.dart` 无问题。正式Release构建 `tmp/desktop-fullscreen-release-android.log` 成功。`publish/Hinge.apk` 版本2.2.0+126，22228887 bytes，SHA256 `F3961462D92F0415176521AF6F5ABE89DD36F214D01E4F1CE4446AD622222228`；签名V2/V3，证书沿用正式证书，ABI仅arm64-v8a；`zipalign -c -P 16 4`通过。
- 构建前旧APK已保留在 `publish/rollback/pre-desktop-fullscreen-20261029/Hinge.apk`，SHA256 `71157947FBBFD21308A7B01D332E8DB2A5F7D2302C835B43DEF5142BBE8C9C60`。未构建Windows包、未操作手机、未推送或发布。
- 待用户自行安装后验收：全屏方向/系统返回恢复、显示桌面与任务视图快捷键、弹出键盘/操作菜单时画面保持，以及竖屏和横屏设备实际显示比例。此轮编译不代表已完成真机视觉或快捷键验收；手机端此前的EOF连接问题也未因这次UI改动获得实机验证。

# 414. 两端连接状态不一致：Windows状态恢复与回调串行化（2026-10-29）

- 用户反馈手机已连接、Windows显示手机离线，手动断开重连后恢复。仅凭该现象不能确定现场唯一触发路径；本轮未通过ADB或其他方式操作手机。
- 确定缺陷：DeviceRegistry.MarkSessionConnected拒绝Disconnected记录，导致有效会话恢复通知无法修复离线记录，必须等新发现包/重连握手先把记录改为Discovered才恢复。移除这一拒绝条件；现有调用均来自会话握手或Connected事件，仍不创建未知设备或改变信任。
- OnPeerIdentified与OnConnectionStateChanged此前可在网络线程并发修改设备状态。现在先投递到Windows UI线程串行处理；执行时重新检查IsSessionReady/读取connection.State，避免排队后的旧事件使用过时状态。断开时原有同设备替代会话检查与清理保护继续保留。
- 新增回归覆盖离线记录无UDP发现包恢复、重复Connected幂等通知及恢复后发现超时不降级；DeviceRegistry定向8项通过。日志tmp/session-state-regression.log。未将代码缺陷与用户此次真实触发条件宣称为已经现场闭环。
- Windows正式脚本构建成功，Setup 229865903 bytes，SHA256 B5852F4C1D2422BB24FB2E4AC69A57628D69C4B9EEF717ABED735C04352031D8；ZIP 161479675 bytes/838项，SHA256 E773F4930BA34D82B0D885776814154A9BFE14F683B35CDE0FEB97320C49261D。完整ZIP读取、DLL/helper与打包输出匹配、Setup内嵌载荷一致检查通过。日志tmp/session-state-release-windows.log，证据tmp/session-state-artifacts.json。
- 按项目要求重构建主线ARM64 APK，版本仍2.2.0+126，SHA256 F3961462D92F0415176521AF6F5ABE89DD36F214D01E4F1CE4446AD622222228；Android源码未改，结果与上一包一致。V2/V3和16KiB对齐通过。修复实际位于Windows，必须安装新版Windows包才能生效。
- 前Setup/ZIP/APK保留于publish/rollback/pre-session-state-20261029。未安装、未推送发布、未恢复已暂停自动化。下一步用户更新并重启Windows后，观察休眠恢复/网络波动/重复连接时两端状态；若仍出现需同一分钟的两端连接诊断，区分旧UI状态和实际半开TCP。
# 415. 横屏桌面默认沉浸布局与隐藏诊断信息（2026-10-29）

- 用户实机截图表明此前仍进入普通横屏页，系统栏、整行控制、卡片边距和诊断文本同时占用空间。#413仅提供额外全屏按钮，不足以满足横屏默认大画面要求；本轮删除旧桌面横屏工具栏布局。
- 电脑桌面进入或旋转到横屏时自动使用沉浸布局、请求隐藏系统栏；画面去掉卡片圆角、边框和外边距，保留窄侧栏。退出全屏或系统返回切回竖屏，显式方向设置在退出整个页面时解除。原纯触控板横屏布局保持原逻辑。
- 性能信息默认关闭，操作菜单增加手动开关；不会自动显示四行统计覆盖桌面。菜单改为滚动列表，短边较小的横屏也能触及全部功能。保留画面原始宽高比，不拉伸或裁掉Windows桌面，比例不同时仍会有必要黑边。
- Flutter定向6项通过，验证横屏直接出现侧栏、画面区域占满高度、诊断默认关闭、游戏覆盖同一Texture/会话和释放输入、旧输入回调隔离及纯触控板交互。静态分析无问题。首次改测试时遗漏ready后的一次布局稳定等待并误改普通触控板入口，已修正测试；未将其视为产品故障。
- 日志tmp/desktop-landscape-tests.log、tmp/desktop-landscape-release-android.log；旧APK保留于publish/rollback/pre-desktop-landscape-20261029。未连接或操作手机，未推送发布。模拟布局不能代替手机实机观感/系统栏行为验收。
- 最终主线Release APK：2.2.0+126，22228887 bytes，SHA256 5E3250835DFB5332778CF233ABFF2E88D737A21CD7E316DB7ABC7D50B35A92D3；V2/V3签名、仅arm64-v8a和16KiB对齐通过。Windows包未改。

# 416. 手机桌面光标不可见时的回退绘制（2026-10-29）

- 用户反馈电脑桌面流在手机上通常看不到光标，电脑鼠标移动后短暂出现，手机远控滑动又消失。找到视频采集端DrawCursor仅在GetCursorInfo返回CURSOR_SHOWING时画系统光标；否则直接返回。因此Windows/目标应用隐藏系统指针时，桌面视频没有可见提示。
- 对DXGI报告为独立指针的帧，系统光标显示时继续绘制原生形状/热点；系统光标隐藏或图标绘制失败时，用GetCursorPos当前屏幕坐标叠加已有轻量箭头。DXGI明确报告指针已经嵌入画面的帧仍不额外叠画，避免双鼠标。仅改选中显示器的视频光标像素，不修改Windows光标可见性或安全设置。
- 合成光标自检通过（6种系统形状、边缘/透明/热点/GDI资源复用，均值0.014ms）；Release安装器/ZIP读完整、打包DLL/helper一致、Setup内嵌载荷哈希通过。真实隐藏光标场景和手机触控体验仍需实机验收。
- Windows Setup 229865945 bytes SHA256 FA822E513183052F8CE3FB097E5A7BC0ADB60DC47443344446E4E7B02C75D40D；ZIP 161479717 bytes/838项 SHA256 CA61C7477EBC9046909A3AAA2C5E80E253970BB8952AD5474C1155847EB4B6C2。证据tmp/desktop-quality-recovery-artifacts.json，日志tmp/cursor-hidden-fallback-windows.log、tmp/cursor-hidden-fallback-android.log。
- Android主APK按项目要求重新构建但源码未变：22228887 bytes SHA256 5E3250835DFB5332778CF233ABFF2E88D737A21CD7E316DB7ABC7D50B35A92D3；arm64-v8a、V2/V3、16KiB对齐均通过。回退包publish/rollback/pre-cursor-hidden-fallback-20261029。未连接/操作手机，未推送发布。
# 417. UU 远程交互调研（2026-10-29）

- 用户要求先检索成熟方案。已完成公开帮助、按键映射/3D视角说明、App Store版本记录与非官方Linux实现对照；详细依据见 D:\App\Hinge\docs\UU_REMOTE_INTERACTION_RESEARCH_20261029.md。
- 重点差距：当前桌面主要是相对鼠标，缺少直接触点定位；缩放依赖手动切换；拖拽/按键状态反馈不足；光标随视频回程；地图操作与锁定鼠标的游戏视角未形成完整模式切换。
- 优先顺序：输入模式及拖拽反馈 → 缩放/滚动仲裁 → 独立光标同步 → 地图/游戏视角切换 → 观看与键盘外设。保留现有纯触控板双指拖拽和游戏多指行为，防止不同页面互相回归。
- 公开文档只能确认公开功能，不将宣传延迟/第三方兼容实现当成官方内部架构；iOS记录不等于Android实测。Android外设文档有Ctrl映射描述矛盾，不照抄；旧文的厂商限制不能直接套用用户Android17手机。
- 本轮仅调研并保存依据，未改产品代码、未构建重复APK、未操作手机。下一轮按研究文档逐项落地并按具体任务验收，不能以按钮存在或模拟测试宣称UU级体验。

# 418. 桌面直接触控、手势仲裁与拖拽反馈（2026-10-29）

- 按#417调研先落地核心桌面输入：操作菜单选择直接触控/鼠标指针，并通过既有native通道按设备保存偏好；新版Windows协商absoluteMouse后默认直接触控，旧电脑不声明能力时保留指针模式并禁用直接触控选项。未新增第三方依赖。
- 协议MouseMove flags=0x0008、x/y=0..65535，仅认证桌面lease接受；拒绝越界和与relative标志混用。Windows先映射所选屏幕，再映射虚拟桌面像素中心，继续验证显示器布局未改变；原触控板与游戏相对鼠标不变。
- 新DesktopGestureSurface统一管理触点、滚动/缩放意图及画面变换；点击逆映射缩放和平移后的有效视频矩形，黑边不注入；双指平移滚动、捏合缩放，超过阈值后锁定本次用途。画面平移保留明确的缩放/平移入口。指针移动积累小数，避免低速滑动丢失位移。
- 直接触控点击定位；移动到拖拽阈值后在起点按下再拖动。指针模式保留双击按住，新增450ms长按拖拽。进入拖拽轻触反馈，并显示左键按住状态；操作菜单提供左键锁定/松开。取消、切换模式、后台、控制失效按原机制释放；尺寸改变清理触点。仅观看禁止鼠标/键盘/游戏入口，显式缩放模式仍可调整手机视口。
- 开发过程问题：自动dart fix意外加入shared_preferences依赖，已删除并离线恢复依赖图，pubspec/lock无内容差异，偏好使用原Android SharedPreferences通道。补丁中断造成重复代码块，已去重且C#/Dart检查通过。旧widget测试原坐标因菜单变长超出屏幕，补滚动与布局等待，未将测试假点击当产品通过。
- Dart定向分析无问题；Flutter 25项（新6项手势+现有桌面/会话/触控板/发送器）通过，最后生命周期微调后相关13项再通过；.NET认证lease/坐标边界/媒体16项通过。日志tmp/desktop-interaction-*-tests.log。
- 待完成：独立于视频的光标元数据/本地绘制、游戏地图与3D视角的自动/手动切换深化、游戏按住左/右/中键转视角与分轴灵敏度、完整特殊键键盘。这些没有作为本轮已实现交付；仍不能宣称达到UU全部体验。
- 两端均需覆盖更新以启用直接触控。未操作手机、未恢复定时任务、未推送GitHub。回退publish/rollback/pre-desktop-interaction-20261029；实机仍需验收点击小图标、缩放后点击、地图拖拽、多屏和VIVO触觉体验。
- 最终产物核验：Windows Setup 229866370 bytes SHA256 8BB93F47AAF44AF8E676A734CAAA09EAB02B0BC740D88009600979C02357F8E4；ZIP 161480142 bytes/838项 SHA256 2E8F14C54BB9859FAED69F3C40B85A49E11AF990F8889058EA0A433FFDA73006；源DLL/helper一致与Setup内嵌载荷通过。Android最终源码后重新构建，22294423 bytes SHA256 0D7FD3245595131931D0680A32174078FF9071710B73C21C74A6DA3D9AC03C84，V2/V3签名、仅arm64-v8a、16KiB对齐通过。报告tmp/desktop-interaction-artifacts.json；构建日志tmp/desktop-interaction-windows-build.log、tmp/desktop-interaction-android-final-build.log。验收表已更新；没有实机验收。

# 422. DXGI 内嵌/隐藏光标时补画回退箭头（2026-10-29）

- 用户反馈手机端仍看不到鼠标。#416只在`PointerPosition.Visible`为真时调用绘制；DXGI把光标视为已经嵌入画面时该字段为假，若此时Windows应用又隐藏系统光标，帧内没有可见光标，之前的`GetCursorPos`备用箭头分支也被整体跳过。
- 依据Microsoft Desktop Duplication文档：硬件叠加指针单独报告；嵌入桌面图像的指针不单独报告。现在仅在系统光标隐藏时对嵌入路径强制画轻量箭头；系统光标可见且已嵌入画面仍不重复绘制，分离指针继续使用真实系统形状/热点。没有改系统光标设置。
- `--verify-pointer`加入四种DXGI/Win32可见状态组合的选择断言，原有边缘裁剪/非法画布检查通过。桌面采集器Release构建通过；包内helper与构建输出相同并复跑自检。
- Windows产品版本`2.3.1.0`：Setup 229867156 bytes SHA256 `E1C4AA465750F400B7548B738F433015ABC2095671A641BE58853F6929FC33FE`；ZIP 161480925 bytes/838项 SHA256 `E60C34229206CA70AEDDAF78146CE7B239A7AE12D200EF1AFB59E499188FDC0D`。内嵌ZIP与便携ZIP哈希相同，helper SHA256 `C18CE85D5E5CD7E7CCEA752574E25936A61B37CD80F02250B140B2A19BFFBAB6`与编译输出一致。
- 按项目流程重建Android主线APK（仅版本元数据变化）：`2.3.1+129`，22294423 bytes SHA256 `F71EA91B210F2AD91B7F39ABB2FEAF75EF74A88106DCC4A8584CBFC3B1C54DB1`；包名、ARM64唯一ABI、V2/V3签名、16KiB对齐通过。Android远端画面代码未改，光标修复需要更新Windows端。
- 回退材料：`publish/rollback/pre-cursor-visibility-followup-20261029`。仅离线构建/包检查，未连接或操作手机、未安装候选、未推送/发布。隐藏光标手机实机验证待用户覆盖更新Windows后确认。日志及产物信息：`tmp/cursor-visibility-followup-*.log`、`tmp/cursor-visibility-followup-artifacts.json`。

# 423. 横屏桌面改为左右对称控制栏（2026-10-29）

- 用户反馈单侧右栏把画面视觉中心推向左侧。全屏横屏改为左右等宽栏，中央`DesktopVideoView`仍按原桌面宽高比居中显示，不拉伸视频；左右按较大的安全区统一预留，单侧缺口下仍保持栏位等宽与视频居中。
- 左栏增加左键、右键、按住/松开拖拽、缩放/平移快捷操作；右栏保留操作菜单、键盘、显示桌面、所有窗口和退出全屏。仅观看时禁止输入类快捷操作，缩放仍可用于本地视口。两侧列表按钮最小高度64dp，短屏可滚动；文字可随系统字体放大。
- `touchpad_screen_test.dart`定向6项通过；全屏测试通过模拟左侧48dp单侧缺口，验证左右栏对称、中心视频可用区域居中及左右点击分别发送正确鼠标按钮。`flutter analyze lib/app/touchpad_screen.dart`无问题。
- 功能候选递增Android `2.4.0+130`、Windows `2.4.0.0`。最终Android主APK 22294423 bytes SHA256 `803174318F8BB19F09A188CC1A9D0DE4B488C8D8E7AC146236C855AD4ADCB2D2`，仅arm64-v8a、V2/V3签名、16KiB对齐通过。Windows Setup 229866808 bytes SHA256 `D7DA36B4ACA979B706BB4F3D16CDA15166EA44C0EFDBC060AE531044CF227A09`；ZIP 161480578 bytes/838项 SHA256 `82CEFA03CD1912D24B615C2BB09903BE0AC06A7DA55139AB00CD2DB527468F56`。Setup内嵌ZIP与便携ZIP一致；桌面视频helper与编译输出一致并通过`--verify-pointer`。
- 回退材料：`publish/rollback/pre-landscape-dual-rails-20261029`。未连接/操作手机、未做实机视觉验收、未推送或发布；需更新Android APK后确认设备实际黑边、字体缩放、缺口安全区和触控手感。日志/产物：`tmp/landscape-dual-rails-*.log`及`tmp/landscape-dual-rails-artifacts.json`。

# 424. 修复双指缩放时桌面视频闪烁（2026-10-29）

> 后续用户复测仍黑帧交替，本节根因判断未证实，补丁未解决问题；已由#425替换。

- `DesktopGestureSurface`把实时桌面`Texture`包在Flutter `Transform`中。Flutter 3.47.1的`RenderTransform.paint`把纯平移（包括单位矩阵）直接绘制并清除层；放大时则走`pushTransform`。捏合在1倍边界来回时会反复切换合成路径，导致闪屏。对应[Flutter 3.47.1源码](https://github.com/flutter/flutter/blob/6655482ec06e547f90abf8ae7590466f4415978d/packages/flutter/lib/src/rendering/proxy_box.dart#L2735)；视频由独立纹理层更新，见[TextureLayer API](https://api.flutter.dev/flutter/rendering/TextureLayer-class.html)。
- 绘制矩阵在逻辑缩放恰好为1倍时使用`1.00001`的不可感知倍率，持续保留同一变换层；触控坐标映射仍使用精确逻辑缩放值，不改变点击落点或缩放边界。
- 新增回归用例先捏合放大再回到1倍，并断言绘制倍率继续略大于1，避免变换层退化成单位/平移矩阵。`desktop_gesture_surface_test.dart`共7项通过，目标文件Flutter静态分析通过。
- Android补丁候选`2.4.1+131`，Windows同步`2.4.1.0`。APK 22294423 bytes SHA256 `E5A49D271069B7B4F792EA71DC4677233F4958706C7554315BF2B44EFCF11C74`，仅arm64-v8a、V2/V3签名及16KiB对齐通过。Windows Setup 229867263 bytes SHA256 `C6A365362097D9A364CE5CD592138040ADB7B2C2F6ADBC7102D776AA3E93FC44`；便携ZIP 161481032 bytes SHA256 `D621C1868C801295F2F132553794933AF07ED391C58DAA4B65FD69D1CF6BCC0D`，安装器内嵌载荷与ZIP逐字节哈希一致。
- 回退包：`publish/rollback/pre-pinch-texture-flicker-20261029`。只做离线测试和打包检查；未安装/操作手机、未做真实VIVO设备的闪烁视觉复测、未推送GitHub或发布。记录：`tmp/pinch-texture-flicker-artifacts.json`。

# 425. 双指缩放黑帧交替：移除实时纹理的Transform路径（2026-10-29）

- 用户确认2.4.1仍出现画面与黑屏交替闪，#424的1倍变换层假设未获实机证实，不能继续当作已定位根因。删除该版1.00001绘制倍率补丁。
- DesktopGestureSurface改用固定裁剪视口和Positioned显示矩形，通过宽高/位置表达缩放和平移，不再对实时Texture施加Transform。手势坐标区域固定；解码Surface分辨率、textureId、视频会话不随捏合修改。保留1–4倍逻辑倍率及原坐标逆映射。
- 回归改为逐帧连续放大、缩小，验证视频显示矩形按预期增长/收回、中心稳定；手势、DesktopVideoView、TouchpadScreen共25项通过，目标文件Flutter静态分析通过。模拟测试不能验证真实GPU合成是否还黑屏。
- Android候选2.4.2+132；Windows保持2.4.1.0，此问题只需更新手机APK。旧手机包保存于publish/rollback/pre-pinch-layout-20261029/Hinge.apk。本轮未操作手机，未推送或发布。实机黑帧验收待用户更新后复测。
- APK最终核验：22294423 bytes，SHA256 6DEFACDDD337DAC54B5F1F3A6C44838610935834A6C928B3D623486C2AD36D25；com.hinge.office / 2.4.2+132，arm64-v8a唯一ABI、V2/V3签名、16KiB对齐通过。

# 426. 缩放大量黑块：迁移Android视频Surface实现（2026-10-29）

- 用户确认2.4.2仍会在缩放时频繁出现大量黑块。#424和#425均未通过实机验收，不能继续把Flutter布局/变换解释为确定根因。
- 原生DesktopVideoBridge由createSurfaceTexture迁移到createSurfaceProducer，交由Flutter选择ImageReader/HardwareBuffer或兼容后端。官方依据：https://docs.flutter.dev/release/breaking-changes/android-surface-plugins 。没有全局禁用Impeller或改变其他页面渲染器。
- 首个有效视频包确定Surface尺寸后才获取Surface并配置解码器，解码格式回调只更新显示比例，不再在绑定MediaCodec后修改Surface缓冲区尺寸。Surface归Producer所有，先停止解码再release Producer，不额外release借用的Surface；Surface失效中断并走既有恢复。会话内视频尺寸变化明确要求重连，避免向过期Surface继续写帧。
- SurfaceProducer没有公开的frame-consumed回调，textureSampling=false，纹理取帧统计显示不可用。ready改为MediaCodec首帧提交Surface后的onFrameRendered通知；这只能证明解码器向Surface输出，不能证明Flutter/GPU物理呈现。保留8秒首帧输出超时和后台停流恢复；不得拿该指标宣称黑块消失。
- 视频视图/统计16项Flutter测试通过，Android正式Release原生Kotlin/R8编译通过；仅arm64-v8a，V2/V3签名和16KiB对齐通过。APK 2.4.3+133，22294423 bytes，SHA256 9F0086050166E18049A47763C421174BC577F3AB674BA02625BCD127650E3144。Windows保持2.4.1.0，只需覆盖更新手机包。
- 上版APK回退目录publish/rollback/pre-surface-producer-20261029。未连接/操作手机、未推送或发布；实际VIVO GPU缩放黑块、画面裁剪/方向、后台恢复仍待实机验收。不能以离线构建或Mock测试宣布根因已证实。


# 427. 统一投屏远控入口与参数交互（2026-10-29）

- Android和Windows设置首页均仅保留“投屏与远控”。统一页按“手机控制电脑”“电脑控制手机”组织：前者进入原有桌面共享、触控板和游戏配置，后者保留无线调试准备/配对、开始/停止和手机画面参数。Windows手机控制电脑页返回统一页，游戏布局继续归属该方向。
- Android删除多段技术教程及错误的旧剪贴板说明，只保留开发者选项状态、打开系统调试设置按钮、同网/配对/可信设备提示。无线调试是系统权限，不新增无法执行的应用内假开关；原远控开关、设备列表和控制状态仍在方向页。统一页从方向页返回后重新读取状态，系统设置返回后继续刷新。
- Windows手机投屏的帧率、缓冲、码率、最长边由NumberBox改为项目已有Slider样式，数值标签实时显示单位，最长边0仍表示原始分辨率。保留原配置键和格式、范围、验证、硬解及编码选项；拖动静止300ms后保存，启动前立即保存；投屏运行时四项参数均禁用，避免缓冲滑块呈现可改却不保存。
- 安卓入口/系统设置/返回状态回归4项通过，四个变更Dart文件静态分析通过。Windows WinUI Release编译通过（0警告/0错误）；WinApp CLI的系统执行别名不可访问，使用项目现有Slider接口和实际XAML编译验证，未把CLI调用失败当验证通过。
- Android 2.5.0+134，Windows 2.5.0.0。包名/版本、仅arm64-v8a、V2/V3签名、16KiB对齐通过；APK和Windows ZIP CRC完整，Setup内嵌ZIP哈希与便携ZIP一致。检查报告tmp/remote-settings-artifacts.json；构建日志tmp/remote-settings-android-build.log、tmp/remote-settings-windows-build.log。
- 回退材料publish/rollback/pre-remote-settings-20261029。未安装候选或操作手机，未做Windows/手机实际界面视觉验收；未推送GitHub或创建Release。本轮仅优化设置与入口，不代表此前缩放黑块已经实机解决。
- 产物SHA256：apk FB6F29C1996E1F11726D014BB849C79A0DA7E50F03AC08A42E61DE8DDA049CC5；setup 243DDEF71C4A4FD9D7392154D5BBDFF91E98ACCA4478406FCEC3DBFD09321F9E；zip 8276319E85963F06C0889DA7DF7EA263557B6CDAF792CD1F24C7A0483DBF7316。


# 428. Windows投屏远控改为对等二级设置（2026-10-29）

- 用户指出#427层级不对等：统一页直接展示手机投屏参数，而另一方向需进入子页。改为独立RemoteSettingsPage统一页，只放“手机投屏到电脑”“手机远控电脑”两个同样式入口和状态摘要；不承载开始/停止、开关、滑块或编码参数。
- 原ScreenControlPage恢复为“手机投屏到电脑”子页；原RemoteInputSettingsPage为“手机远控电脑”子页。两者返回统一页，统一页返回设置首页；游戏布局继续返回其所属远控子页。状态摘要由原运行/授权状态更新，不新建另一套控制或视频会话。原滑块、自动保存、配对和共享配置不变。
- Android仅校正指向Windows子页的路径提示，未改变安卓设置结构。版本Windows 2.5.1.0、Android 2.5.1+135。保留publish/rollback/pre-remote-hierarchy-20261029的上一候选。
- Windows实际WinUI/XAML Release构建通过，0警告/0错误；新页XML结构检查通过。Android主线正式Release构建通过；仅arm64-v8a、V2/V3签名、16KiB对齐、包版本检查通过。APK与Windows ZIP完整性、Setup内嵌ZIP哈希一致。报告tmp/remote-hierarchy-artifacts.json；日志tmp/remote-hierarchy-windows-build.log、tmp/remote-hierarchy-android-build.log。
- 未安装候选、未操作手机、未实机验证界面与点击返回，未推送GitHub或发布。此前缩放黑块仍不属于本轮已验收内容。
- 产物SHA256：apk D81E632E49CD9D635FCF5A6FE797101513871290FD95402A149EF1D64A06F2D5；setup 733164FEAA02A579F3F4751C9D49F5D0ACFD9B8F67FAB421C3EF8DCD60BCB885；zip 64845A0DA2FB2613323A171FCEC4147D5D9B30FF4348333CDD9222A864B9DF79。

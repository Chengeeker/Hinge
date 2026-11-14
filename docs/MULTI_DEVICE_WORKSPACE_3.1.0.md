# Hinge 3.1.0：多设备工作空间改造说明

## 目标
Windows 同时连接多台 Android 手机，不再把“第一台在线手机”作为所有页面的隐式数据源。所有业务写操作均应在执行时明确到 DeviceId，设备离线不可重定向到其他手机在线会话。

## 已实施
- DeviceWorkspaceManager：显式当前工作设备、页面独立设备和设备维度缓存键；保存当前设备到本机设置。
- Windows 导航栏设备选择、首页已连接设备卡片；文件管理保留自己的独立设备标签。
- 笔记、待办、日历、相册加载使用选中的会话；消除离线时回退缓存旧会话的风险。照片数据带 SourceDeviceId。
- 通知历史同时读取多台已连接手机，按来源筛选、按时间排序、分页及去重。通知删除/打开按源设备发送，清空全部需确认。
- 实时通知以设备隔离去重，并把通知操作路由回对应手机；Windows 通知标题展示设备名称，旧式没有来源的操作仅在单台手机在线时允许。
- 电脑发文件/文字需要明确指定一台或多台手机；传输记录按实际接收手机署名。
- 手机内文件右键复制可以粘贴到另一台手机：先通过 Windows 私有临时目录接收，再向目标手机指定存储文件夹发送，完成后清理中转文件，目标手机明确确认。
- 多路 scrcpy 投屏会话按 DeviceId 独立管理；Android DUMP 权限保护下的诊断接口 `/identity` 返回伪名设备 ID，Windows 通过无线 ADB 指定 transport 验证身份后才启动投屏，未匹配的设备绝不误用。
- 剪贴板同步以当前工作手机为唯一桥接目标，并按设备单独 opt-in；切换设备时关闭旧桥接，再验证新手机。
- 应用及安装包更新至 Windows 3.1.0 / Android versionName 3.1.0、versionCode 213。

## 设计和安全约束
- 全局工作设备只决定内容区默认目标。文件管理器的设备标签是页面局部状态，不能影响其他业务模块。
- 同名手机不展示唯一标识符，但路由与缓存使用设备内部 ID。
- 手机离线时不自动改写另一手机数据；多目标发送必须由用户勾选。
- 同一通知 ID、笔记 ID、照片 URI 在不同手机间不可复用为全局唯一键。
- Android DUMP 权限限制了 ADB 设备标识接口访问；该接口不输出 Android_ID 原值或密钥。
- 同时连接多台手机与同时投屏是不同层面的能力；每台手机仍需要有效无线 ADB。
- 跨手机传输经过 Windows 私有暂存空间，不把中转文件留在用户下载目录。

## 实测和局限
- Windows 回归测试包括之前的并发连接与新工作设备切换、通知按源路由策略。
- Android 的设备标识测试和 Release 构建验证仍需使用签名包及真机复核。
- 两台真实手机同时投屏、同时编辑数据、断网重连及跨机大文件压力测试需在两台同时接入时再做。
- 同型号手机的展示名称可相同，但内部 DeviceId 不相同。
- 剪贴板遵循单 Windows 系统剪贴板、单当前工作手机同步设计，不做多手机后台广播同步。
- 本次默认屏幕编码、码率仍为全局偏好；每台手机投屏进程、目标验证和停止按钮已独立。

## 常用命令
Windows:
```powershell
dotnet test windows/Hinge.Tests/Hinge.Tests.csproj --no-restore
dotnet build windows/Hinge.App/Hinge.App.csproj --no-restore
pwsh scripts/build_release_windows.ps1 -TestBuild -TestBuildTag multi-device-workspace-213
```

Android (Gradle 内存提高，避免大型 Compose Debug 测试引发 Kotlin OOM):
```powershell
./gradlew :app:assembleRelease :app:testDebugUnitTest --tests com.hinge.office.HingeDeviceIdentityTest --offline --no-daemon '-Dorg.gradle.jvmargs=-Xmx5G -Dfile.encoding=UTF-8' '-Pkotlin.incremental=false'
```

正式下载及安装包只在项目的 `publish` 目录展示最新版本；历史文件保留于 `BackupRelease`。

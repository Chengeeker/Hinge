# Hinge Windows 设置页面宽度与位置统一验收（2026-10-10）

## 修改内容

六个设置页之前各自把 `StackPanel` 直接交给 `ScrollViewer` 测量。纵向滚动视图给子内容的横向测量约束不一致；仅共享最大宽度时，栈面板仍会按自身期望宽度居中，因此页面起点不同。

新增 `SettingsPageLayout.MatchViewportWidth`，将每个设置页的内容根 Grid 宽度同步为 ScrollViewer 的 `ViewportWidth`，并在加载、尺寸变化时更新。统一使用 `HingeSettingsPageStackStyle`（最大宽度 960 DIP、左右页边距 24 DIP、Stretch 对齐）。覆盖设置总览、传输与连接、个性化、Windows 设置、Cloud Relay、WebDAV。传输页内部输入框/按钮仍遵循原有列布局，不强行改成同宽。

## 实际验收

- Windows Release 构建成功，0 warnings、0 errors。
- Windows Tests：180/180 通过。
- 本机最大化窗口实际运行隔离 Release 候选并用 UI Automation 逐页导航。六页共用内容列起点：屏幕 X=805 px；设置总览入口宽 1200 px。主显示器截图为 2560×1440 物理像素，当前桌面缩放 125%。候选通过复制后的 `Hinge.dll` 将单实例键从 `Hinge.Main` 换成同长的 `Hinge.Test` 隔离；没有改正式输出 DLL，候选退出且原有实例未被关闭。
- 已保存截图：设置总览、个性化、Windows 设置、传输与连接、WebDAV，均位于 `docs/visual-acceptance/20261010-settings-alignment/`。Cloud Relay 页只读取可访问控件位置；未保留该页截图，以免截图记录本地配置凭据。
- 逐页打开设置项及返回操作完成；未点击任何保存、切换、配对、注册或测试操作，故未改变用户设置或服务端数据。
- 布局自检确认 6/6 页面都有共享样式、命名内容根和视口同步调用。

## 构建产物

- Windows 安装器：`publish/windows/Hinge-Setup.exe`，203,878,374 bytes，SHA-256 `2D1C230D6C18E0B86B36E070352D16F2FD203509D4523971880F35FD4735CA5C`；Authenticode `NotSigned`（与项目原有发布方式一致）。
- Windows 便携 ZIP：`publish/windows/Hinge-Windows.zip`，135,494,819 bytes，809 项；内含 `Hinge.dll` 与 Release 输出 SHA-256 一致：`5AF482D5F46EC79C238235953214E5B73812BBAD8E7A7400B0A004A4FB146387`。ZIP SHA-256 `B257263367C430CE2C05239977BC7B5C5F5198B7FAAF5C5858261797A0757091`。
- Android 主线 APK 按项目规程重建（本次 Android 源码未变）；APK 21,159,073 bytes，SHA-256 `B9A370CE39C3BC424F799514F9ADDB2BA36129FD85EF2DDFD9D44D4C1A2E30B3`；apksigner 的 v2/v3 验证通过。

## 回退与未覆盖项

完整回退快照（包括变更前设置页 XAML、开发记录和旧安装产物）：`publish/rollback/pre-settings-page-width-unification-20261010/`。只还原该快照中对应设置页和发布产物即可回到本次统一宽度前的状态；不要重置整个工作树，因为其中还有其他未提交改动。

实际窗口截图/坐标仅覆盖本机当前 125% DPI、最大化、浅色主题。100%/150%、窗口化/窄窗和深色未实际截图验证。构建或布局自检不代表这些环境已验收。

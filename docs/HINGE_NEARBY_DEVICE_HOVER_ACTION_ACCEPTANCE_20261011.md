# 首页附近设备悬停与断开按钮修复（2026-10-11）

## 修改

- 根因：`HomePage.xaml` 只将 `ListViewItemBackground` 普通状态覆盖为透明；Material 3 主题仍为 `PointerOver`、`Pressed`、`Selected` 状态提供灰色/主题状态层。鼠标进入设备列表项后，这个状态层覆盖整行，延伸到断开按钮下方。
- 只在 `DeviceListView.Resources` 覆盖上述五个非普通状态为透明，不影响首页传输记录列表。原有三列行布局、Logo 槽、详情换行和连接事件保持不变。
- `RefreshDeviceList()` 中连接和断开按钮统一复用 `HingeM3FilledButtonStyle`。已连接设备的“断开连接”现在使用与“发送文件”相同的主题主色填充，离线禁用态仍由现有按钮模板处理。
- 未改设备发现、连接/断开实现、业务数据或全局主题。

## 构建与产物

- `dotnet build windows/Hinge.App/Hinge.App.csproj -c Release --no-restore -v:minimal`：成功，0 警告、0 错误。
- `scripts/build_release_windows.ps1`：成功生成安装器及便携 ZIP。ZIP 有 810 项，包内 `Hinge.dll` SHA-256 与 Release 输出一致：`173807ECD780E6714377B607A4F51DEE85966A367FBC64557C3B79DB4CF90417`。
- Android ARM64 Release APK 按仓库脚本重建并通过脚本内签名校验；本次没有修改 Android 源码。
- Windows 安装器 Authenticode 状态仍为 `NotSigned`。

## 视觉验收范围与限制

- 通过 UI Automation 启动工作树候选程序时，单实例键 `Hinge.Main` 将激活请求转交到现有 `D:\Hinge\Hinge.exe`。该旧实例仍连接 vivo X300 Pro。为避免主动断开现有会话，没有关闭或替换运行中的实例。
- 因此没有获得本次候选构建的真实鼠标悬停截图；当前代码已编译，资源覆盖路径未在候选运行实例中完成视觉确认。请勿将现有安装截图视作修复后的验收截图。
- 本轮未点击断开按钮，也未执行设备断开/重连测试；连接业务代码没有改动。

## 回退

- 源码及构建前 Windows/APK 产物快照：`D:\App\Hinge\publish\rollback\pre-nearby-device-hover-20261011\`。
- 回退时只还原 `HomePage.xaml` 与 `MainWindow.xaml.cs`，不要重置整个工作树；如果需要回退交付产物，再还原该目录中的对应安装包/ZIP/APK。

## 产物哈希

- `publish/windows/Hinge-Setup.exe`：`37A8B368E8A5A4CCAF5E368C475BC6045825B865FA177DE159855029A52B79F6`
- `publish/windows/Hinge-Windows.zip`：`7CCC0755D2612299590FE1A137D8872BD26366737E37D78757E49BBEE635B0E4`
- `publish/Hinge.apk`：`7976C4FD921A7A070DCDE81190B109EE7389BFF42D54216823FB964159B21001`
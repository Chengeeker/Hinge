# 第三方依赖许可证审核表 (Third-Party Licenses)

本项目采用 **MIT License**，执行 **MIT First** 依赖准入原则。

## 依赖审核标准
1. **原生系统能力优先**（Android Framework / WinRT / Windows App SDK 系统 API 满足时不引入外部三方库）。
2. **许可证优先等级**：
   - 第一梯队：MIT（优先采纳）
   - 第二梯队：BSD-2-Clause / BSD-3-Clause / ISC / Apache-2.0（需审查 NOTICE 文件）
   - 禁用名单：GPL-2.0 / GPL-3.0 / AGPL / LGPL（有静态链接风险）/ MPL（默认不引入）

## 当前完整依赖登记表 (Phase 10 Release Audit)

| 依赖名称 | 适用平台 | 类型 | 版本 | 许可证 | 引入用途 | 合规状态 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **.NET 8 BCL** | Windows | 运行时 | 8.0+ | MIT | 核心协议栈、Socket 网络、多线程、SHA-256、PDF 生成 | **已核验 (合规)** |
| **Win32 Platform API** | Windows | 系统原生 | System | N/A (Windows OS) | user32/gdi32/shell32 模拟键鼠输入、托盘管理、系统通知 | **已核验 (合规)** |
| **Flutter SDK** | Android | 运行时 | 3.47.x | BSD-3-Clause | 跨平台 Material 3 响应式 UI 框架 | **已核验 (合规)** |
| **crypto** | Android | 运行时 | ^3.0.7 | BSD-3-Clause | 官方标准库 SHA-256、HMAC 凭据派生与文件分块哈希校验 | **已核验 (合规)** |
| **dynamic_color** | Android | 运行时 | ^1.9.0 | Apache-2.0 | Flutter 动态配色接口；Android 原生读取系统 Monet 角色 | **已核验 (合规)** |
| **cupertino_icons** | Android | 运行时 | ^1.0.8 | MIT | 常用辅助矢量图标集 | **已核验 (合规)** |
| **material_symbols_icons** | Android / Windows | 运行时 | 4.2960.0 | Apache-2.0 | Material Symbols Rounded 图标字体，统一双端工作台图标 | **已核验 (合规)** |
| **xunit** | Windows | 开发测试 | 2.5.3 | Apache-2.0 | xUnit 单元与集成测试框架 (开发时依赖，不打包入发布二进制) | **已核验 (合规)** |
| **Microsoft.NET.Test.Sdk** | Windows | 开发测试 | 17.8.0 | MIT | .NET 测试运行时工具集 | **已核验 (合规)** |
| **coverlet.collector** | Windows | 开发测试 | 6.0.0 | MIT | 代码覆盖率分析收集器 | **已核验 (合规)** |
| **flutter_test** | Android | 开发测试 | SDK | BSD-3-Clause | Flutter 官方单元与小部件测试套件 | **已核验 (合规)** |
| **flutter_lints** | Android | 开发代码规范 | ^6.0.0 | BSD-3-Clause | 官方推荐代码风格静态分析规则 | **已核验 (合规)** |

## Windows 设备品牌标识

Windows 首页的设备品牌标识采用 Wikimedia Commons 中对应品牌的 SVG 商标资源，
不在项目内重绘 Logo。透明 SVG 统一放在白色容器中显示。资源来源与页面如下：

- vivo：Wikimedia Commons `Vivo logo 2019.svg`，来源标注为 vivo 官方网站。
- Xiaomi：Wikimedia Commons `Xiaomi logo (2021-).svg`，来源标注为 Xiaomi 官方网站。
- Samsung：Wikimedia Commons `Samsung logo.svg`，来源标注为 Samsung 官方品牌页，CC BY 4.0。
- Huawei：Wikimedia Commons `Huawei wordmark.svg`，来源标注为 Huawei。
- OPPO：Wikimedia Commons `OPPO logo.svg`，来源标注为 OPPO 官方网站。
- HONOR：Wikimedia Commons `Honor Logo (2020).svg`，来源标注为 HONOR 官方网站。

这些标识仍属于各品牌商标；项目只将其用于连接设备的品牌识别展示，不表示与品牌存在合作或背书关系。

## 审计结论 (License Audit Verdict)
- **直接运行时三方库数量**：
  - Windows 端运行时：**0 个外部 NuGet 依赖**（100% 纯 BCL 与操作系统原生能力实现）。
  - Android Flutter 端运行时：引入 `crypto`、`dynamic_color`、`cupertino_icons` 和 `material_symbols_icons`。
- **开源许可证传染性排查**：无任何 GPL / AGPL / LGPL 等具有传染性或商业限制的许可证代码。
- **协议合规性**：项目整体采用宽松的 **MIT 许可证**，与所有直接及间接上游许可证（MIT / BSD-3-Clause / Apache-2.0）完全兼容。

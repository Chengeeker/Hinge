# 依赖准入与审核规范 (Dependency Policy)

## 1. 准入核心准则
每引入一个新依赖包，开发者必须书面回答以下 5 个问题：
1. **操作系统原生 API 能否直接实现？** (若能，严禁引入第三方库)
2. **项目工程内是否已有类似工具或已存在依赖？**
3. **该依赖是否采用 MIT / BSD 宽松许可证？** (GPL/AGPL/MPL 一票否决)
4. **该依赖的体积、活跃度和安全性评级如何？**
5. **是否增加攻击面或维护负担？**

## 2. 依赖管理流程
- 任何新增依赖须在 PR 中提交 docs/dependencies.md 与 THIRD_PARTY_LICENSES.md 的更新。
- 依赖版本统一锁定，禁止使用过于宽泛的版本范围。

## 3. 已准入依赖库清单

| 依赖名称 | 平台 | 许可证 | 准入理由 |
| :--- | :--- | :--- | :--- |
| `crypto` | Android / Dart | BSD-3-Clause | 纯 Dart SHA-256/HMAC 与文件完整性校验 |
| `dynamic_color` | Android / Dart | Apache-2.0 | 读取系统动态配色的 Flutter 接口；实际 Monet 角色由 Android 原生资源桥提供 |
| `material_symbols_icons` | Android / Dart | Apache-2.0 | Android 端 Material Symbols 图标；Windows 端优先使用 WinUI/Segoe MDL2 原生符号 |
| `cupertino_icons` | Android / Dart | MIT | Flutter 工程默认的辅助图标资源，目前仅少量兼容性使用 |
| `.NET BCL` | Windows / C# | MIT | 微软官方运行时基类库 (`System.Security.Cryptography`, `System.Net.Sockets`) |

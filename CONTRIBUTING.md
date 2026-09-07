# 贡献指南 (Contributing to Hinge)

感谢对 Hinge 项目的关注与贡献！为了保证项目的长期可维护性、高稳定性以及严格的开源合规，请遵守以下工作原则：

## 1. 核心开发原则

1. **Native First**：优先使用操作系统原生 API（Android 与 Windows 原生能力优先于引入重量级第三方库）。
2. **MIT First**：严格依赖审核。本项目采用 MIT 协议，严禁引入 GPL/AGPL/MPL 等具有传染性的开源依赖；仅允许引入 MIT/BSD/ISC 等极度宽松的依赖。
3. **LAN First & Offline Capable**：所有跨设备功能必须在无互联网环境下（仅局域网连接）正常工作。严禁引入强制云端、中转服务器或强制用户账户体系。
4. **Vertical Slice (垂直切片交付)**：
   - 禁止单侧过度开发（如仅写完 Android 或仅写完 Windows）。
   - 每一个功能特性都应保证 Android ↔ Protocol ↔ Windows 端到端闭环并配套双端单元测试与协议测试。
5. **Own the Core**：核心通信协议、设备模型、传输核心和状态机由本项目自主掌握。

## 2. 提交流程与规范

- 代码风格遵循各技术栈官方标准规范（Dart linter / C# .editorconfig 规则）。
- 任何新增特性必须提供对应的单元测试或集成验证脚本。
- 引入任何第三方依赖前，必须先更新 THIRD_PARTY_LICENSES.md 和 docs/dependencies.md，并在 PR 中进行依赖必要性说明。

# AI 知识库助手

面向 Windows 10 / 11 的本地通用知识库桌面软件。当前仅完成 BATCH 0，尚无可运行界面或安装包。

资料保存在本机；没有 API Key 也能管理、解析和检索。在线 AI 仅接收当前问题所需片段。

- [架构与技术决策](docs/architecture.md)
- [开发批次与验收](docs/batches.md)
- [BATCH 0 验证报告](docs/batch-0-report.md)

开发依赖：.NET 10 SDK；最终用户使用自包含安装包，不需安装 SDK、Python、Node.js 或数据库服务。

开发命令（由 Agent 执行）：`dotnet build AiKnowledgeAssistant.slnx`。
独立仓库；功能开发使用批次分支。用户文档、凭据、模型缓存和构建产物禁止提交。

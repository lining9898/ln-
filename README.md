# AI 知识库助手

面向 Windows 10 / 11 的本地通用知识库桌面软件。当前完成 BATCH 2：中文 WPF 框架、知识库管理与本地 JSON 持久化。Linux 交叉编译已通过；Windows 运行验证仍待完成，尚无安装包。

产品原则：资料保存在本机；没有 API Key 也应能管理、解析和检索。当前仅开放知识库管理，解析、检索和 AI 按后续批次实现。

- [架构与技术决策](docs/architecture.md)
- [开发批次与验收](docs/batches.md)
- [BATCH 0 验证报告](docs/batch-0-report.md)
- [BATCH 1 验证报告](docs/batch-1-report.md)
- [BATCH 2 验证报告](docs/batch-2-report.md)

开发依赖：.NET 10 SDK；最终用户使用自包含安装包，不需安装 SDK、Python、Node.js 或数据库服务。

开发命令（由 Agent 执行）：`dotnet build AiKnowledgeAssistant.slnx`。
独立仓库；功能开发使用批次分支。用户文档、凭据、模型缓存和构建产物禁止提交。

当前检查：`python3 scripts/validate_batch2.py`。知识库测试：`dotnet run --project tests/AiKnowledgeAssistant.Batch2Checks --configuration Release`。数据目录与导航回归：`dotnet run --project tests/AiKnowledgeAssistant.Batch1Checks --configuration Release`。

`scripts/validate_batch0.py` 与 `scripts/validate_batch1.py` 保留为历史批次检查，部分断言不适用于当前功能状态。

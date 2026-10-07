# AI 知识库助手

面向 Windows 10 / 11 的本地通用知识库桌面软件。当前完成 BATCH 5：中文 WPF 框架、知识库管理、文件导入、可追溯解析与 SQLite 正式数据层。Linux 交叉编译已通过；Windows 运行验证仍待完成，尚无安装包。

产品原则：资料保存在本机；没有 API Key 也应能管理、解析和检索。当前开放知识库管理、文件导入与解析；检索和 AI 按后续批次实现。

- [架构与技术决策](docs/architecture.md)
- [开发批次与验收](docs/batches.md)
- [BATCH 0 验证报告](docs/batch-0-report.md)
- [BATCH 1 验证报告](docs/batch-1-report.md)
- [BATCH 2 验证报告](docs/batch-2-report.md)
- [BATCH 3 验证报告](docs/batch-3-report.md)
- [BATCH 4 验证报告](docs/batch-4-report.md)
- [BATCH 5 验证报告](docs/batch-5-report.md)

开发依赖：.NET 10 SDK；最终用户使用自包含安装包，不需安装 SDK、Python、Node.js 或数据库服务。

开发命令（由 Agent 执行）：`dotnet build AiKnowledgeAssistant.slnx`。
独立仓库；功能开发使用批次分支。用户文档、凭据、模型缓存和构建产物禁止提交。

当前数据层检查：`dotnet run --project tests/AiKnowledgeAssistant.Batch5Checks`；真实解析回归：`dotnet run --project tests/AiKnowledgeAssistant.Batch4Checks --configuration Release`。中文 OCR 测试需开发环境的 Tesseract、pdftoppm 和 `AKA_TEST_TESSDATA` 指向含 chi_sim 与 eng 语言包的目录；最终用户无需执行测试命令。

历史批次静态脚本保留，断言可能不适用于当前功能状态。解析可用不代表检索索引或 AI 问答已实现。

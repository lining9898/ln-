# 旧 JSON 存储

`JsonKnowledgeBaseStore` 与 `JsonDocumentRepository` 仅由 BATCH 5 一次性迁移器读取旧版数据，以及由历史批次回归测试使用。桌面启动与后续业务写入均只使用 SQLite。旧 JSON 保留为迁移备份，不会双写。

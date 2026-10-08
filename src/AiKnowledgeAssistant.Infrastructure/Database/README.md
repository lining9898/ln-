# SQLite 数据层（BATCH 5）

正式数据源：`%LOCALAPPDATA%/AIKnowledgeAssistant/data/databases/knowledge.db`。启动时运行 `SqliteDatabase.Initialize()`、一次性 `LegacyJsonMigration.MigrateIfNeeded()`，然后业务层只使用 `SqliteKnowledgeBaseStore` 与 `SqliteDocumentRepository`。旧 JSON 文件在迁移成功后原样保留作迁移备份；应用运行时不再读写它们。迁移事务连同 `legacy_json_migrated` 标记一起提交，启动重复执行不会重复导入。

`schema_version` 当前为 2。表：`knowledge_bases`、`documents`、`parsed_documents`、`parsed_content`、`parse_failures`、`app_meta`。`parsed_content.content_id` 是独立主键；文本来源位置、TEXT/OCR、错误页和状态均入库。复合外键阻止文档与内容跨知识库关联。每个连接执行 `PRAGMA foreign_keys=ON`；迁移、导入元数据、创建/重命名/删除知识库、开始/完成解析、恢复中断均使用事务。解析替换先在同一事务删除旧结果并插入新结果，失败时保留旧结果。下一次启动恢复中断时保留可用旧解析结果，并标注中断。

启动执行 SQLite `integrity_check` 和 `foreign_key_check`，未知版本/损坏数据库停止启动而不自动覆盖。`Audit()` 输出计数与孤立记录检查。`ProbeCapabilities()` 实际执行 `SELECT sqlite_version()` 与临时 FTS5 表 smoke test。BATCH 6 的版本 2 迁移建立正式全文索引并回填旧解析内容。

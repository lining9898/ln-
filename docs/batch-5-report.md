# BATCH 5：SQLite 正式数据层

日期：2026-10-07。范围仅为数据层切换；未开发搜索、Embedding 或 AI。

## 交付与 Schema

新增 `SqliteDatabase`、`SqliteKnowledgeBaseStore`、`SqliteDocumentRepository`、`LegacyJsonMigration`。正式文件为 `%LOCALAPPDATA%/AIKnowledgeAssistant/data/databases/knowledge.db`，驱动 `Microsoft.Data.Sqlite 10.0.2`，原生 SQLite 包显式升级到 `SQLitePCLRaw.bundle_e_sqlite3 3.0.5` 以避免旧依赖漏洞。`schema_version` 当前为 1，未知版本停止写入，为后续顺序 Migration 留有版本入口。

表：`knowledge_bases`、`documents`、`parsed_documents`、`parsed_content`、`parse_failures`、`app_meta`、`schema_version`。主键分别使用 `knowledge_base_id`、`document_id`、`content_id`；复合外键确保 `parsed_content` 的文档与知识库一致。保存 SHA-256、时间、解析/索引状态、失败原因、PDF 物理页码、TEXT/OCR、章节、段落、行号。业务和 WPF 只通过 Repository/Service 接口访问数据库，不直接写 SQL。

## 迁移与一致性

启动顺序：SQLite 完整性和 schema 检查 → 创建/验证版本 1 → 旧 JSON 单次迁移 → 解析中断恢复。旧 `knowledge-bases.json` v1 与 `documents.json` v1/v2 经原有验证器读取；数据、计数校验、`foreign_key_check`、`legacy_json_migrated` 标记在同一事务提交。迁移失败回滚，可重试；旧 JSON 原样保留且运行时不再写入。若 SQLite 已有数据却缺迁移标记，停止自动迁移，避免覆盖。数据库损坏、未知版本、权限/写入异常均不自动覆盖数据库；桌面显示中文安全错误。

每个连接执行并验证 `PRAGMA foreign_keys=ON`。知识库创建/重命名/删除、文档元数据写入、解析状态、解析结果替换、迁移使用事务。解析替换失败时旧解析内容回滚保留；启动恢复中断解析时有旧结果则继续保留。知识库删除与导入以共享文件锁串行化，外键拒绝孤立文档。`documents/` 受管理副本不参与迁移或修改。

## 实测

BATCH 5 专项测试覆盖旧 JSON v1/v2、中文文件名、多个知识库、同名文件、SHA-256、状态、失败信息、PDF 物理页与 TEXT/OCR、DOCX 章节/段落、TXT/Markdown 行号、幂等重启、JSON 不双写、受管理副本不变、外键、解析替换与迁移中途故障回滚、损坏数据库与未知版本保留。样本审计：`KnowledgeBase count=2`、`Document count=2`、`ParsedContent count=1`、`orphan_document_count=0`、`orphan_content_count=0`、`PRAGMA foreign_key_check=CLEAN`。

实际 SQL 查询 `SELECT sqlite_version()`：`3.53.4`；创建临时 FTS5 表、写入中文并执行 `MATCH`：`FTS5_AVAILABLE=YES`。本批没有搜索功能。BATCH 1–3 回归通过；BATCH 4 共 46 项在 Linux 配置真实 Tesseract 中/英语言包后通过。完整 `.slnx` 的 Linux → Windows 交叉编译通过，0 warning、0 error。Linux 无法执行 WPF 实机 UI，Windows 中文用户目录、文件选择器、拖拽、重启、安装包、Windows Tesseract/PDFium：`BLOCKED_BY_WINDOWS_ENVIRONMENT`；不得视为 PASS。磁盘耗尽等真实硬件故障仅由异常处理覆盖，未做物理故障注入。

`SQLITE_AUTHORITY=YES`；`JSON_RUNTIME_WRITES=NO`；`FOREIGN_KEY_CHECK=CLEAN`；`FTS5_AVAILABLE=YES`。

## BATCH 6（待确认后实施）

在现有 SQLite 版本迁移机制上新增 FTS5 全文索引，对已有解析内容建立索引并维护增量更新；只实现全文检索及来源映射，不提前做 Embedding、AI 或混合检索。

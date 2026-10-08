# BATCH 6 本地全文检索

`SqliteDocumentSearch` 实现 Core 的 `IDocumentSearch`。FTS5 只保存可重建词元，原文和来源只从 `parsed_content`、`documents` 读取。`SearchTerms` 对连续中文生成单字与相邻双字词元，对英文、数字生成规范化词元；查询词元逐一转义并作为 SQL 参数传给 FTS5。双引号/中文引号括住的短语额外回查原文，避免把分散词元误当精确短语。

Schema v2 在事务中创建 `content_fts`，将既有 v1 解析内容回填并更新索引状态。新解析内容与词元在同一事务插入；删除内容触发器清理索引。直接绕过 Repository 更新解析文本会清除旧索引并标记 `PENDING`，需手动重建。`RebuildIndex` 对全部或指定知识库原子重建；`AuditIndex` 比较来源/索引数量、缺失、孤立、词元漂移、状态及外键。

排序先用 FTS5 `bm25`，连续命中完整查询文本额外加权；结果按所选知识库过滤，仅返回已完成或部分完成解析且索引完成的条目。OCR 原文始终保留 OCR 标识。此批没有语义检索或 AI。

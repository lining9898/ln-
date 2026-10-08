# BATCH 6：FTS5 全文检索与来源映射

日期：2026-10-08。本批仅开发本地全文检索；未实现 Embedding、向量、DeepSeek、AI 问答、RAG 或新格式。

## 实现

SQLite `schema_version` 从 1 升至 2。事务创建 `content_fts`，回填旧 `parsed_content`，更新索引状态；失败时保留 v1 原表和解析数据。FTS5 表记录 `content_id`、`knowledge_base_id` 和可重建检索词元；原始文本与可靠来源只从 `parsed_content` 和 `documents` 读取。新解析内容与索引写入同一事务，重新解析原子替换，删除内容触发器清理旧索引。直接绕过 Repository 修改正文会移除旧索引并标记待重建。

默认 FTS5 `unicode61` 无法正确切分连续中文。因此本地生成中文单字与相邻双字词元；英文和数字按词元规范化，查询中的词元转义并参数化。引号包围的精确短语再与原文核对，编号如 `GB 50010 6.2.10` 可准确搜索。排序以 FTS5 `bm25` 为基础，完整查询连续出现在原文时加权；限选定知识库，结果具有 `knowledge_base_id`、`document_id`、`content_id`、文件名、原文、物理 PDF 页码或章节/行号、TEXT/OCR。OCR 标注“机器识别，未经人工核验”。

新增中文“文档搜索”界面，提供知识库选择、搜索、带来源的结果、按库重建索引。切换知识库清除旧结果。索引审计检查来源/索引数量、缺失、孤立、词元漂移、状态与外键。`RebuildIndex` 使用事务，失败不删除原始解析文本。

## 实测文件与结果

用户本批未提供私人验收资料。测试使用仓库中的**真实文件格式、合成测试内容**：`tests/fixtures/batch4/text_pages.pdf`（三物理页、中英文）、`headings_table.docx`、`headings 中文.md`、`lines 中文.txt`，另在临时目录生成英文及数字编号 TXT。它们不是用户真实业务资料，也不代表真实结构规范验收。

BATCH 6 自动检查通过：导入→解析→SQLite→索引→搜索、中文 PDF 物理第一页、英文、数字编号与正确/错误精确短语、多知识库隔离、DOCX 标题与段落、TXT/Markdown 行号、排序、重复解析去重、schema v1 升级回填、重启后搜索、OCR 标识、删除后无旧命中、缺失索引检测、全量及按库重建、直接改动失效、重建失败保留原文，以及搜索 ViewModel 来源展示。首次审计样本：`source=22`、`indexed=22`、`missing=0`、`orphan=0`、`stale=0`。BATCH 1–5 回归通过。完整解决方案 Linux→Windows 交叉编译通过，0 warning、0 error。

Windows 10/11 WPF 实机运行、中文用户目录、文件选择器、真实拖拽、Windows Tesseract 与安装包：`BLOCKED_BY_WINDOWS_ENVIRONMENT`。交叉编译不等于 Windows 运行验收。未用用户提供的结构规范或公司资料进行人工相关性评估。

`FTS5_SEARCH=PASS`；`CHINESE_SEARCH=PASS`；`SOURCE_MAPPING=PASS`；`INDEX_CONSISTENCY=PASS`；`END_TO_END_SEARCH=PASS`；`WINDOWS_RUNTIME=BLOCKED`。

## BATCH 7（待用户确认）

在不改变全文索引权威来源的前提下加入 Windows CPU 可运行的本地 Embedding 与语义检索，并用中文及普通资料样本验证。此报告不授权进入 BATCH 7。

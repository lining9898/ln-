# BATCH 4：文档解析与来源定位

## 1. 四种 Parser

统一 `IDocumentParser`，分别实现 `PdfDocumentParser`、`DocxDocumentParser`、`TextDocumentParser`、`MarkdownDocumentParser`，由 `DocumentParsingService` 选择并协调。
PdfPig 0.1.16 和 DocumentFormat.OpenXml 3.5.1 已固定版本；未引入 Embedding、向量检索、AI 或知识图谱。
导入新文件后后台自动解析；已导入的旧文档可在中文文件列表中选择并点击“解析 / 重新解析”。

## 2. 解析数据模型与来源

`ParsedDocument` 存 document_id、knowledge_base_id、parser_type、可空 page_count、解析单元、失败页及时间。
`ParsedUnit` 存 document_id、knowledge_base_id、sequence、text、source_type、page_number、section_title、section_path、start_line、end_line、paragraph_number、parser_type、created_at。
`ParseFailure` 存失败物理页码和原因。普通文件没有 PDF 页码，不伪造页码；DOCX 使用独立段落序号，不伪造 TXT/Markdown 行号。
当前单元以页、段落或原始文本段为界，不进行面向 Embedding 的复杂分块。
UI 选择文档后显示来源位置、TEXT 或 OCR（机器识别）标记及内容预览；失败原因和失败页可见。

## 3. PDF 物理页

PdfPig 按物理页从 1 开始读取。三页样本的第 2 页空白，第 3 页文本存 `page_number=3`；没有把印刷页码代替物理页码。
空白页不生成虚假的文本单元，`page_count` 仍保留实际总页数。
损坏或不匹配的 PDF 记 FAILED，受管理原始副本保留。

## 4. OCR

无可用文本且含嵌入图像的 PDF 页进入本地 OCR；先用 pdftoppm 渲染指定物理页，再用 Tesseract 的 chi_sim+eng 模型识别。
OCR 单元 `source_type=OCR`，直接文本为 TEXT。每页 OCR 失败单独记录；若其他页成功，整本状态为 PARTIAL，只有失败页则 FAILED。
Linux 测试实际运行了 `/usr/bin/tesseract`、`/usr/bin/pdftoppm` 和临时中文模型；该模型未提交 Git。中文模型来自官方 tessdata_fast，测试文件 SHA-256 为 `a5fcb6f0db1e1d6d8522f39db4e848f05984669172e584e8d76b6b3141e1f730`。
OCR 不等于人工核对：扫描样本中“扫描资料”实际曾识别为“扫拍资料”。来源标记准确，但这一字的内容准确性未通过人工标准。

## 5. DOCX 章节

Open XML 读取正文段落及 Heading 1/2/3，并保存当前标题及层级路径。简单表格按行、单元格转换为文本，保存遍历顺序。
DOCX 没有稳定物理页码；来源使用章节路径与段落/表格行序号。合并单元格、嵌套表格、页眉页脚、浮动对象和自定义标题样式尚未保证正确还原。

## 6. TXT / Markdown

TXT 保留原始段落的起止行号，支持带 BOM 的 UTF-8/UTF-16、UTF-8 和 GB18030 回退；含不可读控制字符的伪文本标为 FAILED。
Markdown 识别 `#`、`##`、`###` 等标题及层级，保留起止行；围栏代码内容仍在解析文本中，代码块内的 `#` 不作为章节标题。

## 7. 状态机与 8. 重新解析

Document.parse_status：PENDING → PARSING → COMPLETED、PARTIAL 或 FAILED。Document.index_status 本批仍为 PENDING。
PARSING 先持久化；提交时文档状态、失败原因和所有解析单元在同一 JSON 快照写入。
重试按 document_id 替换整份旧结果，不叠加重复页/段；解析失败不删除受管理原文件。
如果应用中断，下次启动把遗留 PARSING 标为 FAILED，提示重试，旧结果不会冒充当前版本。
解析中途 JSON 写入失败仍可能暂留 PARSING，需重新启动恢复；Windows 文件替换与断电行为尚待实机验证。

## 9. 实际测试文件

`tests/fixtures/batch4/` 内为工具生成的真实、小型、非私人文件，可重复生成：
- `text_pages.pdf`：三物理页，含中文、英文、空白页。
- `mixed_scan.pdf`：文本页加嵌入图像的中文/英文扫描页。
- `scan_only.pdf`：只有扫描页。
- `headings_table.docx`：Heading 1/2/3、正文、简单表格。
- `empty.docx`：有效但无正文。
- `lines 中文.txt`：中文、英文、空格、空行。
- `headings 中文.md`：三级标题、英文及围栏代码。

另外在隔离临时目录生成空白 TXT、GB18030、二进制伪 TXT、损坏 PDF、内容不匹配的 DOCX。
生成脚本使用开发环境 Python、PyMuPDF、python-docx、Pillow；最终用户不需要这些工具。

## 10. 测试结果

- PASS：BATCH 4 真实解析与 OCR 检查 46 项；独立新进程读取解析状态和 PDF 来源页成功。
- PASS：BATCH 1、2、3 回归分别 12、58、44 项。
- PASS：静态检查 31 项，覆盖四个独立 Parser、固定包版本、真实小文件签名、UI 来源绑定及 Git 忽略。
- PASS：Linux 上 `dotnet build AiKnowledgeAssistant.slnx --configuration Release --nologo --disable-build-servers`，WPF XAML 与 C# 交叉编译成功，0 警告、0 错误。
- PASS：`git diff --check` 及暂存差异检查。

测试没有使用用户私人规范；合成 PDF/DOCX 是真实格式文件，实际通过对应解析器打开。

## 11. 已知限制

OCR 对中文可能识错，所有 OCR 来源明确标记；当前没有 HUMAN_VERIFIED 流程。
PDF 仅对无有效文本且含嵌入图像的页自动 OCR；矢量勾画文字、错误隐藏文本层等可能漏判。
复杂 DOCX 表格、浮动图形与自定义标题样式可能丢失布局或标题语义。普通文档没有条文号也正常解析。
Markdown 以原始文本为主，复杂扩展语法不渲染。GB18030 回退是编码启发，不保证所有旧编码自动正确识别。
本批不建立全文或语义索引；保存的文本尚不能通过文档搜索页检索。真实规范与另一类用户资料的准确性仍需后续验收。

## 12. 编译、13. Windows 阻塞项、14–16. Git

以下均为 **BLOCKED_BY_WINDOWS_ENVIRONMENT**，不标记 PASS：
- Windows 10/11 实机 WPF 自动解析、手动重试、状态与来源预览。
- Windows Tesseract、pdftoppm 原生程序及 chi_sim/eng 模型集成和中文 OCR 实际运行；发行打包在 BATCH 15。
- Windows PDF 解析、DOCX 中文路径、中文用户名与空格路径、离线重启后的结果持久化。
- Windows PDFium 原文查看运行验证；查看器属于 BATCH 9，本批没有实施。
- 高 DPI、长内容布局、键盘操作和用户 UI 确认。

分支 `batch/4-document-parsing`；本批独立 commit（准确 hash 见交付回复），工作区状态见交付回复。模型文件、构建缓存、原始用户资料未提交。

## 17. BATCH 5 计划

建立 SQLite 正式数据源，含知识库、文档、解析结果、来源单元和失败页表，启用外键、事务、版本迁移与 WAL。
从现有 JSON v1/v2 读取并校验 ID、受管理文件、页码和状态，做一致性迁移与失败回滚；迁移成功后停止 JSON 权威写入，避免双权威。
保留 Core 的 Repository 接口，UI 与解析器不因 SQLite 改写。验证重启、跨库隔离、替换式重新解析及备份前的数据库一致性。

BATCH 4 完成后停止，等待用户确认。

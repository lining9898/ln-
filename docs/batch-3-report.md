# BATCH 3：文件导入

## 实现功能

用户在“知识库”页选择知识库后，可在该库的资料区域点击“添加资料”或拖拽文件；“文件管理”页也可选择目标知识库并使用相同导入面板。
文件选择器允许一次选择多个 PDF、DOCX、TXT、MD。逐文件显示成功、重复或失败结果；取消文件选择不写入数据。
文件列表按当前 knowledge_base_id 筛选，显示文件名、类型、大小、导入时间、解析状态和索引状态。
本批状态实际存储为 PENDING，UI 显示“待解析”“待索引”；未运行解析器、Embedding、检索或 AI。
格式检查限文件扩展名，文件内部结构是否有效由 BATCH 4 解析后判断。

含文件知识库暂时不能删除。删除非空库会显示中文说明；空库仍可按 BATCH 2 的二次确认流程删除。
这防止文件记录变成无归属数据。文档删除及连带清理未在本批范围内，需后续单独实现。

## Document 数据模型

- document_id：非空 GUID，每份导入文档唯一。
- knowledge_base_id：目标知识库 GUID；列表、查重和目录均按它隔离。
- original_file_name：原文件名，仅用于显示，不拼入管理目录。
- managed_file_path：软件维护的完整副本路径。
- file_type：PDF、DOCX、TXT、MD。
- file_size：实际复制的字节数，大于 0。
- created_at、updated_at：导入时间戳。
- parse_status、index_status：本批均为 PENDING。
- content_hash：已复制副本的 SHA-256 十六进制值。

Core 提供 Document、IDocumentRepository、IDocumentImporter。
通用架构仍为知识库→文档→页面/章节→Chunk→Source，本批只实现到文档；没有专业专属字段。

## 目录与持久化

受管理副本：`%LOCALAPPDATA%\AIKnowledgeAssistant\data\documents\<knowledge_base_id:N>\<document_id:N>.<扩展名>`。
例如相同原名的两个文件以不同 document_id 保存；跨库文件位于不同 knowledge_base_id 目录。
原文件随后移走或删除，副本仍存在。

本批文档元数据保存在版本化 `databases\documents.json`，知识库元数据仍为 `databases\knowledge-bases.json`。
两者各自是对应实体的唯一权威数据源；没有提前引入 SQLite 或与 SQLite 双写。
BATCH 5 通过 Repository 接口切换到 SQLite，迁移后 JSON 不再承担权威写入。

## 重复文件与数据一致性

在目标库内按复制得到的 SHA-256 查重；同名不同内容允许，异名相同内容提示并跳过。
跨知识库允许同一文件，各有独立元数据记录和受管理副本。

导入先确认知识库存在，持有知识库级锁贯穿复制及元数据提交；删除也需同一锁。
复制到本库隐藏暂存文件时同步计算 SHA-256。0 字节或重复内容不会形成记录；暂存文件清理。
正式副本放好后才写 JSON 元数据，避免正常失败留下“有记录、无文件”。
JSON 写入采用同目录临时文件、Flush(true)、替换和独占锁。
写入失败后回读：确认无记录才删除正式副本；若记录已提交则视为成功并保留副本；若无法确认则保留副本并提示人工检查。
损坏/未知版本的目录拒绝读取和写入，不以空目录覆盖；发现副本缺失或大小不符时拒绝把记录当成正常文件。
测试注入的复制失败、无读取权限和元数据失败都未留下记录或孤立文件。

仍有实质限制：进程崩溃或断电可能留下未登记的暂存文件或副本；清理权限同时失败时也可能留下孤立文件。本批没有自动恢复或清理器，不宣称零孤儿文件。受管理副本被外部修改但大小不变时，本批不会每次列表时重新计算 SHA-256。

## 验证

环境：Linux，.NET SDK 10.0.401；测试文件均由程序在独立临时目录生成并清理，不包含用户资料。

- PASS：`python3 scripts/validate_batch3.py`，24 项静态检查：XAML/XML、中文五入口、文档面板、拖拽事件、列表字段、WPF 路线及 Git 忽略。
- PASS：`dotnet run --project tests/AiKnowledgeAssistant.Batch3Checks --configuration Release --disable-build-servers`，44 项逻辑检查。覆盖四种扩展名、中文与空格/较长文件名、同名不同内容、异名同内容、同库重复、跨库相同文件、空/不存在/不支持、模拟读取权限及复制失败、元数据失败与提交后异常、锁冲突、删除保护和数据损坏检测；导入 A 期间切换到 B，不在 B 显示 A 的文件或导入结果。
- PASS：独立子进程重开同一文档目录，核验记录数量、受管理副本及 SHA-256。它验证跨进程持久化，不等同 Windows 软件重启。
- PASS：BATCH 1 回归 12 项，BATCH 2 回归 58 项。
- PASS：`dotnet build AiKnowledgeAssistant.slnx --configuration Release --nologo --disable-build-servers`，C# 与 WPF XAML Release 交叉编译成功，0 警告、0 错误。
- PASS：`git diff --check` 与暂存差异格式检查。

测试使用扩展名符合要求的最小模拟字节，不宣称真实 PDF/DOCX 结构有效；真实解析留待 BATCH 4。

## Windows 未验证

以下均为 **BLOCKED_BY_WINDOWS_ENVIRONMENT**，不标记 PASS：

- Windows 10/11 实机打开文件选择器、选择多个文件、取消选择及真实拖拽。
- 中文用户名、中文/空格路径、超长文件名和 Windows 路径限制。
- 实机受管理目录权限、文件占用、并发锁、替换操作及磁盘空间不足。
- 软件退出重开、电脑重启后列表显示、长列表布局、高 DPI 与键盘操作。
- Windows 无 SDK 离线运行及 UI 截图确认。

## 文件变更、Git 与下一批

本批新增 Core 文档模型/接口，Infrastructure 文档 JSON 仓库、路径、流式复制与导入服务，Desktop 文档 ViewModel 与共用面板，以及 BATCH 3 测试、静态检查和报告。
修改知识库仓库的导入/删除协调、应用装配、主窗口、ViewModel 连接、既有测试项目引用、README 与架构文档。
实际逐文件清单可用 `git show --stat <本批 commit>` 核对；准确 commit hash 见交付回复。

分支：`batch/3-document-import`。本批独立 commit，未创建远程仓库，未提交用户资料、缓存或模拟导入文件。

BATCH 4 计划：对已导入 PDF、DOCX、TXT、MD 提取文本，识别 PDF 页与普通文档章节/行号，保存 TEXT/OCR 来源和解析结果，失败可重试；使用真实文件核对提取质量与定位。继续不建立全文或语义索引，不调用 AI。
BATCH 3 完成后停止，等待用户确认。

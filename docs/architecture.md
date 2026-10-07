# BATCH 0 架构决策

## 技术栈与边界

C#、.NET 10 LTS、WPF、MVVM；目标 Windows 10 22H2 / Windows 11，首发 x64 CPU。
WPF 使用 Windows 原生桌面控件，不依赖浏览器、Node.js、Python、Docker 或服务器。
Windows 10 的操作系统维护状态独立于应用兼容性；实际支持范围以 BATCH 16 实测为准。
不提前引入复杂框架或服务容器。BATCH 1 使用轻量 MVVM；异步解析任务避免阻塞 UI。

三个程序集即可：Core 为领域模型及接口；Infrastructure 为本地与网络适配器；Desktop 为 WPF UI 与协调。
模块目录按职责划分，无需每个目录单独建立项目。依赖方向 Desktop → Infrastructure → Core，Desktop 可直接引用 Core。
BATCH 0 时 Desktop 为项目骨架库；BATCH 1 已加入 WPF 应用入口，OutputType 为 WinExe。

## 通用数据模型

KnowledgeBase → Document → DocumentSection/Page → Chunk → SourceLocator。
所有文档、文本块和索引记录关联 KnowledgeBaseId；每次检索显式传入选定知识库 ID。
Document 保存名称、格式、大小、导入时间、原始路径、受管理副本路径、解析/索引状态、错误、可空页数、内容哈希和 AllowCloudSend。
Chunk 保存原文、序号、知识库/文档 ID、来源位置、TEXT/OCR/HUMAN_VERIFIED、扩展元数据。
SourceLocator 区分 PDF 页码、DOCX 段落/标题、TXT/MD 行号；非 PDF 不伪造稳定页码。
规范编号、条文号仅存可选 JSON 扩展元数据，不设专业专属核心字段。

## 本地数据库

Microsoft.Data.Sqlite + SQLite FTS5；版本化迁移、外键、事务和 WAL。
首版一个本地数据库，按 KnowledgeBaseId 逻辑隔离；无数据库服务。
跨库查询显式筛选 ID，删除库级联清理关系、索引与受管理副本。
BATCH 2/3 先用可替换 JSON 管理元数据，BATCH 4 使用解析结果文件；BATCH 5 一次性迁移到 SQLite，验证后才删除旧数据。
这遵守用户批次顺序，避免提前实现 SQLite 或双写两套存储。

## 解析与 OCR

PDF：优先 PdfPig 提取按页文本；DOCX：DocumentFormat.OpenXml 提取段落、标题及表格文本。
TXT/Markdown：保留行号与标题；编码优先 BOM/UTF-8，失败时明确处理 GB18030，不能静默丢字。
保留原始副本；解析结果带解析器版本。按标题、段落、页面分块并保留重叠与页段映射。
扫描或混合 PDF 按页检测文本质量；仅需要的页面渲染后使用本地 Tesseract 中文/英文 OCR。
OCR 原生组件和语言包随发行版提供；验证 Windows 集成、授权及体积后锁定版本。
OCR 页面/片段明确标记 OCR；不能将 OCR 成功等同人工核对。密码保护、损坏文件与 OCR 失败显示可重试原因。

## 检索、Embedding 与向量

FTS5 + 中文字符二元分词/拉丁词分词，保留原文并增加规范化检索列。
中文不能依赖默认 unicode61 分词。原文精确匹配/短语提升优先级，数字和编号必须专项回归。
本地语义候选：ONNX Runtime CPU + multilingual-e5-small（多语言，384 维）。
使用一致的 tokenizer、query:/passage: 前缀、池化和归一化；模型及许可证随发行包附带，离线首次启动无需下载。
模型版本和分块版本入索引，升级可重建；候选需通过真实中文资料召回与 CPU 时延测试，未通过则更换，不能宣称效果已达标。
首版向量保存 SQLite BLOB，按所选知识库读取向量并计算余弦相似度；无向量数据库服务器。
默认小中型本地资料规模，先测 1 万/5 万块；规模超标再决定是否引入 ANN，当前不提前实现。
全文和向量通过 RRF 融合，结合精确命中和文档/来源类型过滤。无模型时全文检索继续工作并提示语义索引未就绪。

## AI 与隐私

统一 IAIProvider；DeepSeek 为首个 OpenAI-compatible HTTP 适配器，服务地址/模型作为提供者设置。
使用 HttpClient，取消、超时与受控重试；不直接绑定知识库和解析器。
密钥存 Windows Credential Manager，普通配置仅保存凭据引用；不写源码、配置、Git 或日志。
连接测试不附带文档。模型列表可配置，候选 deepseek-chat，发布前核验官方接口。
RAG 只发送当前问题和选定库的必要片段，限制块数及总上下文预算；发送前可查看确切资料。
AllowCloudSend=false 的文档可本地检索但排除云端上下文。
片段分配不可伪造的本次来源 ID；输出引用映射本地 SourceLocator，拒绝不存在的来源 ID。
缺少足够证据直接提示“当前选择的知识库中未检索到足够依据。”检索文本视为资料，不允许其中指令改变系统行为。
无 Key 只禁用生成，不影响本地功能。回答质量与逐项引用仍需真实资料验收。

## 原文查看

PDF 候选 PDFium 本地渲染 + WPF 页视图；原生 Windows x64 二进制随包附带，先验证维护性和许可证。
引用保存 PDF 物理页码（从 1 开始），印刷页码可另存标签；点击跳物理页。
上一/下一页、页码跳转、缩放、提取文本搜索；扫描页搜索依赖 OCR，命中定位能力按提取坐标逐步验证。
DOCX 可打开受管理原文件并预览提取文本；TXT/MD 本地文本预览。禁用 Markdown 的远程资源/脚本。

## 存储、升级与备份

%LOCALAPPDATA%\AIKnowledgeAssistant\data\ 下为 databases/、documents/、indexes/、cache/、config/。
原文件按知识库/文档 ID 存储，显示原始名称；避免依赖用户原始文件的后续位置。
程序与数据分离，升级不删除数据；初始化/迁移失败不覆盖旧库。
设置不含 Key；凭据独立位于 Windows Credential Manager。
备份 ZIP 含版本化 manifest、SQLite 一致快照、原文件、解析数据和必要索引版本信息；不含 Key/可再生成缓存。
恢复先校验哈希、版本、大小和路径穿越，再暂存/事务导入；默认不覆盖已有库。

## 打包与验证

.NET self-contained win-x64 便携目录先交付，后使用 Inno Setup 生成中文 Setup.exe。
不强求单文件；OCR、PDFium、ONNX 原生库和模型必须齐全，包体以实测报告。
第三方依赖在对应批次锁版本、记录许可证；代码签名取决于可用证书，无证书不得宣称已签名。
Windows 真实验收覆盖无开发运行时、中文用户名/路径、空格路径、重启、离线、安装升级/卸载保留数据。
当前 Linux 环境不能证明 WPF 可运行或 Windows 安装成功。

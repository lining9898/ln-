# BATCH 2：知识库管理

## 完成范围

已实现新建、重命名、删除、列表、刷新与本地持久化；启动读取已有知识库。
中文知识库页面显示列表、数量、空状态和“尚未导入文件”，不伪造文件数量或索引状态。
新建/重命名通过简洁中文模态窗口操作，取消不保存，校验失败保留输入并显示中文原因。
必须选择库后才能重命名/删除；删除显示名称并二次确认，默认“否”。取消不触发存储删除。
操作成功更新列表，重命名保持 ID，删除后清除选择。
不实现文件导入、文档解析、全文检索或 AI；其他批次操作保持禁用。

## 数据模型和边界

KnowledgeBase：knowledge_base_id（非空 GUID）、name、created_at、updated_at。
业务操作按 ID 查找，重命名不更换 ID，名称不用于路径拼接。
保持通用知识库→文档→页面/章节→文本块→来源的架构，本批仅实现知识库一级，其余模型未提前实现。
核心无结构专业、GB 编号或条文专属字段。

名称：Trim 首尾空白，保留内部空格，Unicode NFC 规范化；1–80 个 Unicode 标量。
拒绝空白、超长和规范化后仍含控制字符的名称。
创建/重命名不允许同名，比较不区分大小写；自身原名重命名视为无操作。

## 持久化与删除安全

本批不引入 SQLite，遵守 BATCH 5 数据层实施顺序。
%LOCALAPPDATA%\AIKnowledgeAssistant\data\databases\knowledge-bases.json：
- schema_version=1
- knowledge_bases：上述通用记录列表

IKnowledgeBaseStore 为 Core 接口，JsonKnowledgeBaseStore 为 Infrastructure 实现。
BATCH 5 迁移时保留 ID、名称及时间戳；不是两套数据双写。
每次操作读取最新快照，独占 .lock 文件串行读改写；竞争时显示重试提示，不能用陈旧列表覆盖。
同目录临时文件写入并 Flush(true)，再替换目标；清理正常操作的临时文件。
损坏 JSON、缺失必需顶层字段、未知版本、重复 ID/名称、无效元数据拒绝读取及写入，保留原文件。
读取失败显示中文错误并保留当前可见列表，不静默清空数据。

删除只移除匹配 ID 的元数据。本批没有文档数据，不递归删除文件目录。
测试确认删除 A 后 B 的 ID/名称/时间戳不变，并保留 B 的模拟目录文件。
未来文档级关联清理必须在相应批次独立实现与验证。
原子替换、文件锁、权限及断电行为在 Windows 上尚待验证；不宣称断电零丢失。

## 实际测试结果

环境 Linux，.NET SDK 10.0.401。

PASS：python3 scripts/validate_batch2.py，20 项静态检查。
覆盖项目/XAML XML、五个入口、知识库命令绑定、其他按钮禁用、中文、WPF 路线、Core 边界、删除默认否及忽略规则。

PASS：dotnet run --project tests/AiKnowledgeAssistant.Batch2Checks --configuration Release --disable-build-servers，58 项逻辑检查。
覆盖中文/空格/空名称/超长/重复/NFC/大小写/Unicode 边界，创建/重命名非法输入不写入，唯一 ID、其他库不变，取消创建/删除，命令与列表更新，锁冲突，损坏及未知版本保护，元数据异常和 UI 错误处理。
独立子进程重新打开同一元数据文件，核验 ID、重命名结果及列表数量；不只重新建立同一进程内的对象。
所有文件位于独立临时目录，测试后清理，不读取私人资料。

PASS：BATCH 1 逻辑回归 12 项，数据目录、保留文件和导航仍正常。
PASS：最终 dotnet build AiKnowledgeAssistant.slnx --configuration Release --nologo --disable-build-servers，三个项目及 WPF XAML 编译成功，0 警告、0 错误。
PASS：git diff --check 及暂存差异格式检查。

测试期间发现 UI 异常分类遗漏 InvalidDataException，导致损坏文件提示测试失败；已修复并完整重跑通过。未把失败测试记为 PASS。
普通沙箱首次编译返回失败且无诊断；允许执行环境下完成最终编译与测试，报告仅以最终可复现结果为准。

## Windows 阻塞项

以下状态均为 BLOCKED_BY_WINDOWS_ENVIRONMENT：
- Windows 10/11 实机启动、操作五个入口及知识库列表。
- 名称窗口、中文输入法、错误消息、取消/保存和真实删除二次确认（含默认“否”）。
- 软件退出并重开、电脑重启后列表与 ID 持久化。
- 中文用户名、中文/空格真实 Windows 路径、权限和锁冲突。
- 高 DPI、键盘焦点、长名称显示、UI 截图确认。
- Windows 文件替换与故障/断电行为、离线真实启动。

Linux 新进程及逻辑测试不替代以上 Windows 实机验收。

## 实际文件变更

新增 14 个：
- Core/KnowledgeBase：KnowledgeBase.cs、KnowledgeBaseName.cs、IKnowledgeBaseStore.cs
- Infrastructure/Storage：JsonKnowledgeBaseStore.cs
- Desktop/ViewModels：KnowledgeBasesViewModel.cs、IKnowledgeBaseDialogs.cs、RelayCommand.cs
- Desktop/UI：KnowledgeBaseDialogs.cs、KnowledgeBaseNameDialog.xaml、KnowledgeBaseNameDialog.xaml.cs
- tests/AiKnowledgeAssistant.Batch2Checks：项目文件、Program.cs
- scripts/validate_batch2.py
- docs/batch-2-report.md

修改 6 个：README.md、docs/architecture.md、Desktop/App.xaml.cs、Desktop/MainWindow.xaml、Desktop/ViewModels/MainViewModel.cs、BATCH 1 测试项目文件。
上述 Core、Infrastructure、Desktop 路径均位于 src/AiKnowledgeAssistant.<模块>/ 下。

## Git 与下一批

分支：batch/2-knowledge-base-management；本批独立提交，准确 Commit hash 见交付回复与 git log。
SDK、构建缓存、运行数据、用户资料不进入 Git；未创建远程仓库或发布软件。

BATCH 3 计划：选择目标知识库、选择/拖拽 PDF/DOCX/TXT/MD，复制到按知识库 ID 与文档 ID 隔离的受管理目录，记录通用文件元数据、列表与状态；测试中文/空格路径、重复导入、目标库隔离、取消和复制失败。
不在 BATCH 3 提取文本或建立索引，待 BATCH 4 解析。必须补充知识库删除与新文档关联的清理规则及安全测试。

BATCH 2 后停止，等待用户确认。

# BATCH 1 完成报告

## 范围和 UI

保持 C# / .NET 10 / WPF，不实现 BATCH 2 及以后功能。
左侧固定五个入口：AI问答、知识库、文档搜索、文件管理、设置。
右侧通过 ViewModel 属性与 XAML DataTrigger 切换；默认 AI问答。
中文标题、中文空状态、微软雅黑 UI 字体、浅色办公布局，无演示文档和伪造回答。

AI问答：知识库选择空状态、聊天空状态、问题输入框与发送按钮。
知识库：我的知识库、禁用的新建按钮、通用资料类别提示。
文档搜索：禁用的搜索框和搜索按钮、无资料提示。
文件管理：禁用的添加资料按钮、无文件提示。
设置：可复制的只读实际数据路径、隐私说明、AI 未配置说明。
未实现的操作明确禁用；键盘导航使用原生 ListBox。内容可滚动，设置最小窗口尺寸。
没有实际 Windows 截图；不可据此断言布局、高 DPI 或无障碍体验已通过。

## 数据目录

Core 定义 IUserDataPaths；Infrastructure 的 UserDataPaths 使用 SpecialFolder.LocalApplicationData。
默认根目录为 %LOCALAPPDATA%\AIKnowledgeAssistant\data，含 databases/documents/indexes/cache/config。
App 启动时创建目录，目录已存在时不删除或覆盖内容。不存数据库、知识库或凭据。
拒绝空/相对基础路径，创建失败向启动层报告，显示中文错误后退出，不静默换路径。
路径通过 Path.Combine 组合；不拼接用户名，不在程序目录保存用户数据，不使用工作目录作为数据根目录。
设置页显示实际路径；没有密钥输入或明文密钥配置。

## 新增/修改文件

新增：
- src/AiKnowledgeAssistant.Core/Storage/IUserDataPaths.cs
- src/AiKnowledgeAssistant.Infrastructure/Storage/UserDataPaths.cs
- src/AiKnowledgeAssistant.Desktop/App.xaml
- src/AiKnowledgeAssistant.Desktop/App.xaml.cs
- src/AiKnowledgeAssistant.Desktop/MainWindow.xaml
- src/AiKnowledgeAssistant.Desktop/MainWindow.xaml.cs
- src/AiKnowledgeAssistant.Desktop/ViewModels/MainViewModel.cs
- tests/AiKnowledgeAssistant.Batch1Checks/AiKnowledgeAssistant.Batch1Checks.csproj
- tests/AiKnowledgeAssistant.Batch1Checks/Program.cs
- scripts/validate_batch1.py
- docs/batch-1-report.md

修改：
- src/AiKnowledgeAssistant.Desktop/AiKnowledgeAssistant.Desktop.csproj（WinExe 入口）
- README.md（当前状态、验证命令及历史检查适用范围）
- docs/architecture.md（桌面入口状态更新）

## 实际验证

环境：Linux；开发用 SDK 10.0.401 安装于 /tmp/aka-dotnet，不进入仓库或用户发行包。
普通沙箱代理下载失败，使用获准执行环境完成官方 SDK 下载和引用包恢复；未变更技术路线。

PASS：python3 scripts/validate_batch1.py，16 项静态检查。
覆盖 XML、五个页面绑定、操作禁用、中文语言、滚动布局、WPF 目标、Core 边界及 Git 忽略。

PASS：dotnet build AiKnowledgeAssistant.slnx --configuration Release --nologo。
三个项目恢复与交叉编译成功，包含 WPF XAML 编译，0 警告、0 错误。
这只证明 Linux 上编译成功，不能证明 Windows 上能启动。

PASS：dotnet run --project tests/AiKnowledgeAssistant.Batch1Checks --configuration Release。
12 项实际 C# 检查通过：中文与空格临时路径、目录创建、重复初始化保留文件、服务重建路径一致、非法路径拒绝、创建失败上报、导航列表/默认页/通知/非法与重复选择、设置路径。
测试编译复用同一 MainViewModel 源文件，不模拟另一套业务代码；结束清理独立临时目录。
中文/空格路径结果仅来自 Linux，不替代 Windows 路径验收。

PASS：git diff --check；源码与 Git 忽略检查。构建缓存和 SDK 未提交。

## Windows 未完成验证

以下均为 BLOCKED_BY_WINDOWS_ENVIRONMENT，绝不标记 PASS：
- Windows 10 / 11 实机 WPF 启动、退出与实际五页导航操作。
- 键盘焦点、输入法、屏幕阅读与显示字体。
- 高 DPI、缩放、最小窗口、不同分辨率的真实布局。
- Windows 中文用户名、中文/空格数据路径与权限错误消息验证。
- Windows 应用重启、电脑重启后数据目录稳定性。
- Windows 无 SDK 环境运行与离线启动（自包含发行在 BATCH 15 完成）。
- 实际 UI 截图、用户 UI 确认。

## Git 与下一批

工作分支 batch/1-desktop-shell；本批独立 commit，准确 hash 见 git log 和交付回复。
未创建远程仓库或发布安装包。
下一批 BATCH 2：知识库新建、重命名、删除、列表与独立 ID；使用可迁移本地元数据存储，验证重启持久化、删除边界和中文名称。不实现文件导入/SQLite/AI。
本批完成后停止，等待用户指示。

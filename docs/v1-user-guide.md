# AI 知识库助手 V1 使用说明

## 安装与数据

Windows 10 x64 可运行 `AIKnowledgeAssistantSetup-win-x64.exe` 安装。便携版解压 `AIKnowledgeAssistant-win-x64.zip`，运行 `AIKnowledgeAssistant\AiKnowledgeAssistant.Desktop.exe`。两种版本共用当前 Windows 用户的本地数据目录：`%LOCALAPPDATA%\AIKnowledgeAssistant\data`。卸载程序不会删除该目录；升级前建议先备份。

## 知识库和文档

在“知识库”中新建并选择知识库，然后在“文件管理”或知识库中的资料区域添加 PDF、DOCX、TXT、MD 文件。扫描 PDF 会使用本地中文 OCR，解析完成后可在“文档搜索”输入中文关键词。选择结果并点击“打开来源”，查看 PDF 物理页码；可用“上一页”“跳转”“下一页”浏览提取文本或 OCR 文本。

OCR 是机器识别结果，重要内容请对照原始 PDF 核验。当前来源面板显示页码和文字，不渲染原始 PDF 页面；需要查看页面版式时，可使用来源面板显示的本地文件路径，在 PDF 阅读器中打开对应页。

## AI 问答

在“设置”中输入自己的 DeepSeek API Key 并点击“保存 Key”。密钥保存到 Windows Credential Manager；输入框在保存后清空。点击“测试连接”会向 DeepSeek 发起一次真实请求，可能产生费用。在“AI问答”中选择知识库、输入问题并发送。联网问答会传送检索到的相关片段；涉及私人资料时，请自行决定是否使用。没有 Key 时本地导入与搜索仍可使用。当前版本的 Key 输入框在输入时可见，请注意旁观者。

## 备份与恢复

关闭软件后，可将整个 `%LOCALAPPDATA%\AIKnowledgeAssistant\data` 目录复制到安全位置。恢复时请先退出软件；为避免覆盖现有资料，建议先另存当前数据目录，再由熟悉文件恢复操作的人核对备份后恢复。项目备份服务已经过独立目录恢复测试，但 V1 尚未提供图形界面的备份/恢复按钮。

## 问题处理

导入后若仍显示“待解析”，点击“解析 / 重新解析”。搜索不到内容时先检查解析和索引状态，再尝试“重建索引”。网络问答失败时检查 Key、网络与账户余额。请勿将个人文档、数据库或 API Key 发到公开 issue。

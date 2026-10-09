# 0.2.0 联网搜索与 PDF 原始引用

日期：2026-10-09。分支：windows-real-validation-batch6。

## 交付行为

- 默认仅检索所选知识库；开启“知识库＋联网”后，用明确填写的公开搜索词查询 Tavily，将网页片段和本地片段交给 DeepSeek 综合回答。
- 公网搜索只接收公开搜索词，不接收私人文档正文或独立的问题输入；搜索 Key 存入 Windows Credential Manager。
- 本地来源编号为 S，网页来源编号为 W；来源区域显示真实返回的 URL 与可用发布日期，不接受模型编造的来源编号。
- AI 的 PDF 引用和文档搜索均可打开本地原始 PDF 对应物理页，支持翻页与缩放。页面由程序自带的 Poppler 渲染，文件不改写。

## 实际验证

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 搜索请求与综合回答逻辑 | PASS | WebChecks 使用模拟 HTTP 响应验证公开搜索词、鉴权头、片段限制、本地与网页上下文合并、错误提示和来源映射；不是公网实测。 |
| 真实安装版 WPF | PASS | 中文知识库搜索“墙板”得到 33 条结果；实点联网开关、搜索 Key 密码框及 PDF 原始页面入口。 |
| 原始 PDF 页面 | PASS | 对真实 CECS PDF 的物理第 10 页执行 Poppler 渲染，人工查看本地截图，版式与中文可读。 |
| AI 引用按钮与翻页 | PASS | CitationUiChecks 使用测试引用及真实 PDF，调用真实 WPF 自动化按钮打开第 10 页，再翻至第 11 页；未调用 AI。 |
| 真实联网综合回答 | BLOCKED | Tavily 搜索 Key 和真实 API 调用授权未提供，未使用未知 Key 或产生付费调用。 |

API 实现依据：https://docs.tavily.com/documentation/api-reference/endpoint/search 。联网失败时会明确报错，不静默退回本地后假称公网检索成功。来源存在性核对不等于事实正确性核对。

交付文件：`artifacts/windows/AIKnowledgeAssistantSetup-win-x64.exe`、`artifacts/windows/AIKnowledgeAssistant-win-x64.zip`。具体代码提交与 SHA-256 见同目录 `release-manifest.txt`。原始 PDF 的本地验收截图在 `artifacts/windows/pdf-original-ui.png`，包含用户资料，不进入 Git。

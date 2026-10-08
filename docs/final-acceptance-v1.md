# FINAL-ACCEPTANCE V1

日期：2026-10-08。平台：Windows 10 x64。分支：`windows-real-validation-batch6`。安装器版本：0.1.1。

交付文件：`artifacts/windows/AIKnowledgeAssistantSetup-win-x64.exe`（SHA-256 `86AF85EF21C9563FE9716F2E417FED0FBF337F1B2D7CB045C07AF4233BC1E114`）；`artifacts/windows/AIKnowledgeAssistant-win-x64.zip`（SHA-256 `261F3A5748DE71D52331C81E23425CD6DC54F9E66B6B2CE2D552EEAFF4B2820C`）。

| 项目 | 状态 | 实际证据 |
| --- | --- | --- |
| DEVELOPMENT_COMPLETE | PASS | BATCH 1-14 回归通过；安装版已运行。 |
| AUTOMATED_TESTS_PASS | PASS | BATCH 1-14 回归与 BATCH 9 来源查看回归通过。 |
| WINDOWS_UI_VERIFIED | PASS | Windows UI Automation 直接操作最终安装版和便携版 WPF：选择中文知识库、搜索“墙板”得到 33 条可见结果、打开来源、跳到 PDF 物理第 10/58 页、核对“蒸压加气混凝土墙板”、打开设置、验证空 Key 保存和无 Key 连接错误、正常关闭。安装版重启后复测通过。 |
| OCR_STATUS | PASS | 两份真实 PDF 重新解析；SQLite 中 OCR 中文正常，分别 40 页与 58 页。 |
| CHINESE_SEARCH_STATUS | PASS | 真实数据库中文检索，以及上述 WPF 界面检索均通过。 |
| PDF_JUMP_STATUS | PASS | WPF 物理页码跳转与该页 OCR 正文相符。原始 PDF 版式渲染尚无。 |
| BACKUP_RESTORE_STATUS | PASS | 真实用户资料备份到 ZIP，在独立中文空格目录恢复；用户原数据未覆盖。 |
| INSTALLER_STATUS | PASS | 安装、启动、卸载重装、便携版中文空格路径均已验证；卸载后用户数据库仍在。 |
| DEEPSEEK_LIVE_VERIFIED | BLOCKED | 未提供真实 API Key，未发起付费联网请求。无 Key 的 WPF 提示与 Credential Manager 服务测试通过。 |
| RELEASE_READY | BLOCKED | 可交付 V1 试用版；正式发布仍需真实 DeepSeek 问答及人工核对原始 PDF 页面。 |

UI 脚本：`scripts/final-ui-acceptance.ps1`。此脚本只输入本地搜索词和无 Key 空值，不录入密钥或文档内容；检测到已配置 Key 时跳过连接测试。前次服务验收包括 FTS5、知识库隔离、数据持久化、引用核验和备份恢复；UI 验收未替代这些服务测试。OCR 来源页曾显示 PDF 内嵌水印文字，现已修复并对真实 WPF 页码跳转回归。代码审查未发现默认日志写入密钥、问题、回答或文档正文；这属于静态检查，不代表对外部诊断工具的审计。

剩余人工验收：在“文档搜索”选择一条 PDF 结果，点击“打开来源”，对照外部 PDF 阅读器核对物理页码和版式；如需使用联网问答，在“设置”中亲自输入 Key，再测试一次连接、问答和引用原文。不要通过聊天发送 Key。

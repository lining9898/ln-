using System.Security.Cryptography;
using System.Diagnostics;
using System.Reflection;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Infrastructure.Documents;
using AiKnowledgeAssistant.Infrastructure.OCR;
using AiKnowledgeAssistant.Infrastructure.Parser;
using AiKnowledgeAssistant.Infrastructure.Storage;
using AiKnowledgeAssistant.Desktop.ViewModels;
using System.Text.Json.Nodes;

if (args.Length > 0 && args[0] == "--probe")
{
    var reopened = new JsonDocumentRepository(new UserDataPaths(args[1]));
    var documentId = Guid.Parse(args[2]);
    var result = reopened.GetParsed(documentId);
    if (reopened.Get(documentId)?.ParseStatus != ProcessingStatus.Completed ||
        result?.Units.Select(u => u.PageNumber).SequenceEqual([1, 3]) != true)
        return 2;
    Console.WriteLine("独立进程读取解析状态与物理 PDF 页码成功");
    return 0;
}
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
void Reject<T>(Action action, string name) where T : Exception
{
    var rejected = false;
    try { action(); } catch (T) { rejected = true; }
    Check(rejected, name);
}
var repoRoot = new DirectoryInfo(AppContext.BaseDirectory);
while (repoRoot is not null && !Directory.Exists(Path.Combine(repoRoot.FullName, "tests", "fixtures", "batch4")))
    repoRoot = repoRoot.Parent;
if (repoRoot is null) throw new InvalidOperationException("未找到真实测试文件。");
var fixtureRoot = Path.Combine(repoRoot.FullName, "tests", "fixtures", "batch4");
var tempRoot = Path.Combine(Path.GetTempPath(), "aka-batch4-" + Guid.NewGuid().ToString("N"));
try
{
    var paths = new UserDataPaths(Path.Combine(tempRoot, "中文用户 空格"));
    paths.EnsureDirectories();
    var kb = new JsonKnowledgeBaseStore(paths);
    var a = kb.Create("测试知识库 A");
    var b = kb.Create("公司资料 B");
    var repo = new JsonDocumentRepository(paths);
    var importer = new DocumentImportService(kb, repo, paths);
    Document Import(string file, Guid id) => importer.Import(id, Path.Combine(fixtureRoot, file));
    var pdf = Import("text_pages.pdf", a.Id);
    var mixed = Import("mixed_scan.pdf", a.Id);
    var scanOnly = Import("scan_only.pdf", a.Id);
    var docx = Import("headings_table.docx", a.Id);
    var emptyDocx = Import("empty.docx", a.Id);
    var txt = Import("lines 中文.txt", a.Id);
    var markdown = Import("headings 中文.md", a.Id);
    var otherKbPdf = Import("text_pages.pdf", b.Id);
    var parsers = new IDocumentParser[] {
        new PdfDocumentParser(new FailingOcr()), new DocxDocumentParser(),
        new TextDocumentParser(), new MarkdownDocumentParser()
    };
    Check(parsers.Select(p => p.ParserType).SequenceEqual(["PDF","DOCX","TXT","MARKDOWN"]) &&
        new[] { "PDF","DOCX","TXT","MD" }.All(t => parsers.Count(p => p.Supports(t)) == 1),
        "四种格式有独立 IDocumentParser 实现");
    var service = new DocumentParsingService(repo, repo, parsers);
    Check(repo.List(a.Id).All(d => d.ParseStatus == ProcessingStatus.Pending), "导入后为 PENDING");
    var originalPdfHash = SHA256.HashData(File.ReadAllBytes(pdf.ManagedFilePath));
    var parsedPdf = service.Reparse(pdf.Id);
    var pdfResult = repo.GetParsed(pdf.Id)!;
    Check(parsedPdf.ParseStatus == ProcessingStatus.Completed && parsedPdf.TotalPages == 3 &&
        pdfResult.PageCount == 3, "文本 PDF 三个物理页，解析状态 COMPLETED");
    Check(pdfResult.Units.Select(u => u.PageNumber).SequenceEqual([1,3]) &&
        pdfResult.Units.All(u => u.SourceType == SourceType.Text) &&
        pdfResult.Failures.Count == 0, "空白物理第 2 页跳过；第 3 页保留 page_number=3");
    Check(pdfResult.Units.Any(u => u.Text.Contains("中文第一章", StringComparison.Ordinal)) &&
        pdfResult.Units.Any(u => u.Text.Contains("English last page", StringComparison.Ordinal)),
        "文本 PDF 提取中英文内容");
    Check(pdfResult.Units.All(u => u.DocumentId == pdf.Id && u.KnowledgeBaseId == a.Id &&
        u.ParserType == "PDF" && u.CreatedAt != default), "PDF 单元包含可追溯的文档、库与解析器信息");
    Check(SHA256.HashData(File.ReadAllBytes(pdf.ManagedFilePath)).SequenceEqual(originalPdfHash),
        "解析不修改受管理原始副本");
    var probe = new ProcessStartInfo(Environment.ProcessPath!)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!) == "dotnet")
        probe.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    probe.ArgumentList.Add("--probe");
    probe.ArgumentList.Add(Path.Combine(tempRoot, "中文用户 空格"));
    probe.ArgumentList.Add(pdf.Id.ToString());
    using (var child = Process.Start(probe)!)
    {
        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        Check(child.WaitForExit(30000) && child.ExitCode == 0,
            "独立新进程保留来源页码与状态：" + output + error);
    }
    var partial = service.Reparse(mixed.Id);
    var partialResult = repo.GetParsed(mixed.Id)!;
    Check(partial.ParseStatus == ProcessingStatus.Partial && partial.ParseError!.Contains("第 2 页", StringComparison.Ordinal) &&
        partialResult.Units.Single().PageNumber == 1 && partialResult.Failures.Single().PageNumber == 2,
        "文本页成功而扫描页 OCR 失败时为 PARTIAL，并记录失败物理页");
    var scanFailure = service.Reparse(scanOnly.Id);
    Check(scanFailure.ParseStatus == ProcessingStatus.Failed &&
        repo.GetParsed(scanOnly.Id)!.Failures.Single().PageNumber == 1,
        "只有扫描页且 OCR 失败时为 FAILED，失败页明确保存");
    var unavailable = new DocumentParsingService(repo, repo,
        [new PdfDocumentParser(new TesseractPdfPageOcr("/missing-pdftoppm", "/missing-tesseract"))]);
    Check(unavailable.Reparse(mixed.Id).ParseStatus == ProcessingStatus.Partial,
        "OCR 工具不可用不会把整本混合 PDF 误标 COMPLETED");
    var tessdata = Environment.GetEnvironmentVariable("AKA_TEST_TESSDATA");
    if (!string.IsNullOrWhiteSpace(tessdata))
    {
        var pdftoppm = Environment.GetEnvironmentVariable("AKA_TEST_PDFTOPPM") ?? "pdftoppm";
        var tesseract = Environment.GetEnvironmentVariable("AKA_TEST_TESSERACT") ??
            (OperatingSystem.IsWindows() ? @"C:\Program Files\Tesseract-OCR\tesseract.exe" : "tesseract");
        var actual = new DocumentParsingService(repo, repo,
            [new PdfDocumentParser(new TesseractPdfPageOcr(pdftoppm, tesseract, tessdata))]);
        var ocrDocument = actual.Reparse(mixed.Id);
        var ocrResult = repo.GetParsed(mixed.Id)!;
        Check(ocrDocument.ParseStatus == ProcessingStatus.Completed &&
            ocrResult.Units.Count == 2 && ocrResult.Units[0].SourceType == SourceType.Text &&
            ocrResult.Units[1].SourceType == SourceType.Ocr && ocrResult.Units[1].PageNumber == 2,
            "真实本地 Tesseract：混合 PDF 正确区分 TEXT/OCR 与物理页");
        Check(ocrResult.Units[1].Text.Any(c => c is >= '\u4e00' and <= '\u9fff') &&
            ocrResult.Units[1].Text.Contains("English", StringComparison.Ordinal),
            "真实扫描页 OCR 提取中英文（中文正确率单独人工核对）");
        Check(actual.Reparse(scanOnly.Id).ParseStatus == ProcessingStatus.Completed &&
            repo.GetParsed(scanOnly.Id)!.Units.Single().SourceType == SourceType.Ocr,
            "只有扫描页的 PDF 使用 OCR，来源不伪装为 TEXT");
        actual.Reparse(mixed.Id);
        Check(repo.GetParsed(mixed.Id)!.Units.Count == 2 &&
            repo.GetParsed(mixed.Id)!.Units.Select(u => u.Sequence).SequenceEqual([1,2]),
            "重复解析原子替换旧页，不产生重复记录");
    }
    else Console.WriteLine("BLOCKED：没有配置 AKA_TEST_TESSDATA，未执行真实中文 OCR。 ");
    var docxDone = service.Reparse(docx.Id);
    var docxResult = repo.GetParsed(docx.Id)!;
    Check(docxDone.ParseStatus == ProcessingStatus.Completed &&
        docxResult.Units.Any(u => u.SectionTitle == "第一章 公司资料") &&
        docxResult.Units.Any(u => u.SectionTitle == "第二节 技术说明"), "DOCX 提取 Heading 1/2 标题");
    Check(docxResult.Units.Any(u => u.SectionTitle == "小节 三级标题" &&
        u.SectionPath!.Contains("第一章 公司资料 > 第二节 技术说明 > 小节 三级标题", StringComparison.Ordinal)),
        "DOCX Heading 3 保留章节层级路径");
    Check(docxResult.Units.Any(u => u.Text.Contains("中文正文 English paragraph", StringComparison.Ordinal)) &&
        docxResult.Units.All(u => u.ParagraphNumber > 0 && u.StartLine is null && u.EndLine is null),
        "DOCX 普通段落提取并保留段落顺序定位");
    Check(docxResult.Units.Any(u => u.Text.Contains("测试 A | 表格中文内容", StringComparison.Ordinal)),
        "DOCX 简单表格按行转为可读文本");
    Check(service.Reparse(emptyDocx.Id).ParseStatus == ProcessingStatus.Failed &&
        repo.GetParsed(emptyDocx.Id)!.Units.Count == 0, "空 DOCX 标记 FAILED，无伪造解析单元");
    var txtDone = service.Reparse(txt.Id);
    var txtResult = repo.GetParsed(txt.Id)!;
    Check(txtDone.ParseStatus == ProcessingStatus.Completed &&
        txtResult.Units.Select(u => (u.StartLine, u.EndLine)).SequenceEqual([(1,2),(4,4)]) &&
        txtResult.Units[0].Text.Contains("中文第一行", StringComparison.Ordinal),
        "TXT 原始段落保存真实行号范围和中文");
    var mdDone = service.Reparse(markdown.Id);
    var mdResult = repo.GetParsed(markdown.Id)!;
    Check(mdDone.ParseStatus == ProcessingStatus.Completed &&
        mdResult.Units.Any(u => u.SectionTitle == "第一章 总览") &&
        mdResult.Units.Any(u => u.SectionTitle == "第二节 细节"), "Markdown # / ## 标题识别");
    Check(mdResult.Units.Any(u => u.SectionTitle == "第三层 标题" &&
        u.SectionPath!.Contains("第一章 总览 > 第二节 细节 > 第三层 标题", StringComparison.Ordinal)),
        "Markdown ### 标题保留层级");
    Check(mdResult.Units.Any(u => u.Text.Contains("# code heading should stay text", StringComparison.Ordinal) &&
        u.SectionTitle == "第二节 细节" && u.StartLine > 0 && u.EndLine >= u.StartLine),
        "Markdown 代码块内容不丢失，伪标题不改变章节");
    Check(mdResult.Units.All(u => u.SourceType == SourceType.Text && u.PageNumber is null &&
        u.DocumentId == markdown.Id && u.KnowledgeBaseId == a.Id), "Markdown 行号和来源归属完整");
    var emptyPath = Path.Combine(tempRoot, "空.txt"); File.WriteAllText(emptyPath, "  \n\n");
    var emptyTxt = importer.Import(a.Id, emptyPath);
    Check(service.Reparse(emptyTxt.Id).ParseStatus == ProcessingStatus.Failed,
        "只有空白字符的 TXT 标记 FAILED");
    var damagedPdfPath = Path.Combine(tempRoot, "损坏.pdf"); File.WriteAllText(damagedPdfPath, "not a pdf");
    var damagedPdf = importer.Import(a.Id, damagedPdfPath);
    Check(service.Reparse(damagedPdf.Id).ParseStatus == ProcessingStatus.Failed &&
        File.Exists(damagedPdf.ManagedFilePath), "损坏 PDF 标记 FAILED 且保留受管理副本");
    var fakeDocxPath = Path.Combine(tempRoot, "扩展名不匹配.docx"); File.WriteAllText(fakeDocxPath, "plain text");
    var fakeDocx = importer.Import(a.Id, fakeDocxPath);
    Check(service.Reparse(fakeDocx.Id).ParseStatus == ProcessingStatus.Failed,
        "DOCX 扩展名与内容不匹配时 FAILED");
    var otherDone = service.Reparse(otherKbPdf.Id);
    Check(otherDone.ParseStatus == ProcessingStatus.Completed &&
        repo.GetParsed(otherKbPdf.Id)!.Units.All(u => u.KnowledgeBaseId == b.Id &&
        u.DocumentId == otherKbPdf.Id) &&
        repo.GetParsed(pdf.Id)!.Units.All(u => u.KnowledgeBaseId == a.Id),
        "知识库隔离和文档隔离均保持");
    repo.BeginParsing(txt.Id);
    Check(repo.Get(txt.Id)!.ParseStatus == ProcessingStatus.Parsing,
        "PARSING 中间状态持久化");
    Reject<InvalidOperationException>(() => repo.BeginParsing(txt.Id), "同一文档禁止并发解析");
    repo.RecoverInterruptedParsing();
    Check(repo.Get(txt.Id)!.ParseStatus == ProcessingStatus.Failed &&
        repo.GetParsed(txt.Id) is null && repo.Get(txt.Id)!.ParseError!.Contains("中断", StringComparison.Ordinal),
        "启动恢复中断状态为 FAILED，旧结果不会冒充当前版本");
    Check(service.Reparse(txt.Id).ParseStatus == ProcessingStatus.Completed &&
        repo.GetParsed(txt.Id)!.Units.Count == txtResult.Units.Count,
        "失败后重新解析替换结果，不叠加重复单元");
    var viewModel = new DocumentsViewModel(repo, importer, service, repo);
    viewModel.SelectKnowledgeBase(a);
    var autoFile = Path.Combine(tempRoot, "自动解析.txt");
    File.WriteAllText(autoFile, "自动解析中文内容 English");
    await viewModel.ImportFilesAsync([autoFile]);
    var autoDocument = repo.List(a.Id).Single(d => d.OriginalFileName == "自动解析.txt");
    Check(autoDocument.ParseStatus == ProcessingStatus.Completed &&
        viewModel.Items.Any(d => d.Document.Id == autoDocument.Id && d.ParseStatus == "已完成"),
        "UI 导入文件后自动解析并显示实际状态");
    viewModel.SelectedDocument = viewModel.Items.Single(d => d.Document.Id == autoDocument.Id);
    Check(viewModel.ParsedUnits.Single().Unit.DocumentId == autoDocument.Id &&
        viewModel.ParsedUnits.Single().Position.Contains("第 1-1 行", StringComparison.Ordinal),
        "UI 选中文档后显示可追溯来源与内容预览");
    await viewModel.ParseSelectedAsync();
    Check(repo.GetParsed(autoDocument.Id)!.Units.Count == 1 &&
        viewModel.ImportSummary.Contains("重新解析完成", StringComparison.Ordinal),
        "UI 手动重新解析替换旧单元");
    viewModel.SelectKnowledgeBase(b);
    Check(viewModel.ParsedUnits.Count == 0 &&
        viewModel.Items.All(d => d.Document.KnowledgeBaseId == b.Id),
        "UI 切库清除其他库来源预览");
    var gbPath = Path.Combine(tempRoot, "GB18030文本.txt");
    File.WriteAllBytes(gbPath, System.Text.Encoding.GetEncoding("GB18030").GetBytes("公司中文资料"));
    var gbDocument = importer.Import(a.Id, gbPath);
    Check(service.Reparse(gbDocument.Id).ParseStatus == ProcessingStatus.Completed &&
        repo.GetParsed(gbDocument.Id)!.Units.Single().Text == "公司中文资料",
        "GB18030 中文 TXT 可解析且保留原文");
    var binaryPath = Path.Combine(tempRoot, "伪文本.txt");
    File.WriteAllBytes(binaryPath, [0, 1, 2, 3]);
    var binaryDocument = importer.Import(a.Id, binaryPath);
    Check(service.Reparse(binaryDocument.Id).ParseStatus == ProcessingStatus.Failed,
        "含不可读控制字符的伪 TXT 拒绝伪造成功");
    var failingViewModel = new DocumentsViewModel(repo, importer, new FailingParsingService(), repo);
    failingViewModel.SelectKnowledgeBase(a);
    var parseErrorPath = Path.Combine(tempRoot, "解析工具失败.txt");
    File.WriteAllText(parseErrorPath, "已成功导入但解析器故障");
    await failingViewModel.ImportFilesAsync([parseErrorPath]);
    Check(failingViewModel.ImportSummary.Contains("成功 1", StringComparison.Ordinal) &&
        failingViewModel.ImportSummary.Contains("失败 0", StringComparison.Ordinal) &&
        failingViewModel.ImportResults.Single().Contains("已导入，但解析未完成", StringComparison.Ordinal) &&
        repo.List(a.Id).Any(d => d.OriginalFileName == "解析工具失败.txt"),
        "UI 区分导入成功与后续解析器故障");
    var catalog = File.ReadAllText(Path.Combine(paths.Databases, "documents.json"));
    Check(catalog.Contains("\"schema_version\": 2", StringComparison.Ordinal) &&
        (string.IsNullOrWhiteSpace(tessdata) || catalog.Contains("\"source_type\": \"OCR\"", StringComparison.Ordinal)) &&
        catalog.Contains("\"parse_status\": \"COMPLETED\"", StringComparison.Ordinal),
        string.IsNullOrWhiteSpace(tessdata)
            ? "版本化存储实际写入解析数据和状态"
            : "版本化存储实际写入解析数据、来源类型和状态");
    var catalogPath = Path.Combine(paths.Databases, "documents.json");
    var malformed = JsonNode.Parse(catalog)!.AsObject();
    malformed.Remove("parsed_documents");
    File.WriteAllText(catalogPath, malformed.ToJsonString());
    Reject<InvalidDataException>(() => repo.List(a.Id), "v2 缺失解析结果字段时拒绝覆盖");
    var damaged = JsonNode.Parse(catalog)!.AsObject();
    var parsedEntries = damaged["parsed_documents"]!.AsArray();
    var pdfEntry = parsedEntries.Single(x => x!["document_id"]!.GetValue<string>() == pdf.Id.ToString())!;
    pdfEntry["units"]![0]!["page_number"] = 999;
    File.WriteAllText(catalogPath, damaged.ToJsonString());
    Reject<InvalidDataException>(() => repo.List(a.Id), "越界物理页来源数据拒绝读取");
    var missingSource = JsonNode.Parse(catalog)!.AsObject();
    var entries = missingSource["parsed_documents"]!.AsArray();
    entries.Remove(entries.Single(x => x!["document_id"]!.GetValue<string>() == pdf.Id.ToString()));
    File.WriteAllText(catalogPath, missingSource.ToJsonString());
    Reject<InvalidDataException>(() => repo.List(a.Id), "文档标记已完成但来源缺失时拒绝伪造成功");
    File.WriteAllText(catalogPath, catalog);
    var legacy = JsonNode.Parse(catalog)!.AsObject();
    legacy["schema_version"] = 1;
    legacy.Remove("parsed_documents");
    foreach (var record in legacy["documents"]!.AsArray())
    {
        record!["parse_status"] = "PENDING";
        record["parse_error"] = null;
        record["total_pages"] = null;
    }
    File.WriteAllText(catalogPath, legacy.ToJsonString());
    var legacyCount = repo.List(a.Id).Count;
    Check(legacyCount > 0 && repo.GetParsed(pdf.Id) is null,
        "BATCH 3 schema v1 目录可按旧文档记录读取");
    Check(service.Reparse(pdf.Id).ParseStatus == ProcessingStatus.Completed &&
        repo.List(a.Id).Count == legacyCount &&
        File.ReadAllText(catalogPath).Contains("\"schema_version\": 2", StringComparison.Ordinal),
        "首次解析将 v1 原子升级 v2，文档数与 ID 不变");
    Console.WriteLine($"完成 {checks} 项 BATCH 4 检查；不代表 Windows 实机验证。");
    return 0;
}
finally { Directory.Delete(tempRoot, true); }

sealed class FailingOcr : IPdfPageOcr
{
    public string Recognize(string pdfPath, int physicalPageNumber) =>
        throw new IOException("故障注入：OCR 不可用");
}
sealed class FailingParsingService : IDocumentParsingService
{
    public Document Reparse(Guid documentId) => throw new IOException("模拟解析器启动失败");
}

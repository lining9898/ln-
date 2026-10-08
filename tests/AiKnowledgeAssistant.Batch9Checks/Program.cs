using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Desktop.ViewModels;
using AiKnowledgeAssistant.Infrastructure.Database;
using AiKnowledgeAssistant.Infrastructure.Documents;
using AiKnowledgeAssistant.Infrastructure.OCR;
using AiKnowledgeAssistant.Infrastructure.Parser;
using AiKnowledgeAssistant.Infrastructure.Retrieval;
using AiKnowledgeAssistant.Infrastructure.Storage;
using AiKnowledgeAssistant.Infrastructure.Viewer;

var root = Path.Combine(Path.GetTempPath(), "aka-source-viewer-中文 空格-" + Guid.NewGuid().ToString("N"));
var failures = new List<string>();
void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
    if (!condition) failures.Add(name);
}
void Reject<T>(Action action, string name) where T : Exception
{
    var rejected = false;
    try { action(); } catch (T) { rejected = true; }
    Check(rejected, name);
}

try
{
    var paths = new UserDataPaths(root); paths.EnsureDirectories();
    var database = new SqliteDatabase(paths); database.Initialize();
    new LegacyJsonMigration(database).MigrateIfNeeded();
    var bases = new SqliteKnowledgeBaseStore(database);
    var docs = new SqliteDocumentRepository(database);
    var importer = new DocumentImportService(bases, docs, paths);
    var parsing = new DocumentParsingService(docs, docs, [
        new PdfDocumentParser(new TesseractPdfPageOcr("/missing-pdftoppm", "/missing-tesseract")),
        new DocxDocumentParser(), new TextDocumentParser(), new MarkdownDocumentParser()]);
    var search = new SqliteDocumentSearch(database);
    var viewer = new LocalSourceViewer(docs, docs);
    var kb = bases.Create("来源核验库");
    string fixture(string name) => Path.GetFullPath(Path.Combine("tests", "fixtures", "batch4", name));
    var pdf = importer.Import(kb.Id, fixture("text_pages.pdf"));
    Check(parsing.Reparse(pdf.Id).ParseStatus == ProcessingStatus.Completed, "PDF 解析完成");
    var txtPath = Path.Combine(root, "中文 行号.txt");
    File.WriteAllText(txtPath, "第一行中文\n第二行来源定位\n");
    var txt = importer.Import(kb.Id, txtPath);
    Check(parsing.Reparse(txt.Id).ParseStatus == ProcessingStatus.Completed, "TXT 解析完成");
    var pdfHit = search.Search("中文第一章", [kb.Id]).First(h => h.DocumentId == pdf.Id);
    var opened = viewer.Open(pdfHit);
    Check(opened.DocumentId == pdf.Id && opened.CurrentPage == 1 && opened.PageCount == 3 &&
        opened.ManagedFilePath == pdf.ManagedFilePath && opened.Text.Contains("中文第一章"),
        "从搜索命中打开 PDF 原文件物理页");
    var page3 = viewer.Jump(pdf.Id, 3);
    Check(page3.CurrentPage == 3 && page3.Text.Length > 0,
        "PDF 原文查看支持物理页码跳转");
    Reject<ArgumentOutOfRangeException>(() => viewer.Jump(pdf.Id, 99), "越界 PDF 页码拒绝跳转");
    var txtHit = search.Search("来源定位", [kb.Id]).First(h => h.DocumentId == txt.Id);
    var txtView = viewer.Open(txtHit);
    Check(txtView.CurrentPage is null && txtView.Position.Contains("第 1-2 行", StringComparison.Ordinal) &&
        txtView.Text.Contains("第二行来源定位"),
        "非 PDF 来源打开行号定位文本");
    Guid? selectedKb = kb.Id;
    var vm = new DocumentSearchViewModel(search, () => selectedKb, viewer) { Query = "中文第一章" };
    vm.SearchCommand.Execute(null);
    vm.OpenSourceCommand.Execute(null);
    Check(vm.HasSource && vm.SourcePosition.Contains("PDF 物理第 1 页", StringComparison.Ordinal) &&
        vm.SourceText.Contains("中文第一章", StringComparison.Ordinal),
        "搜索 ViewModel 打开来源预览");
    vm.JumpPage = "3";
    vm.JumpPageCommand.Execute(null);
    Check(vm.SourcePosition.Contains("PDF 物理第 3 页", StringComparison.Ordinal) &&
        vm.SourceText.Length > 0,
        "搜索 ViewModel 支持页码跳转");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAILURES=" + string.Join(",", failures));
    Environment.Exit(1);
}

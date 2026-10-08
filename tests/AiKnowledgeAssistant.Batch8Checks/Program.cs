using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Infrastructure.Database;
using AiKnowledgeAssistant.Infrastructure.Documents;
using AiKnowledgeAssistant.Infrastructure.Embedding;
using AiKnowledgeAssistant.Infrastructure.OCR;
using AiKnowledgeAssistant.Infrastructure.Parser;
using AiKnowledgeAssistant.Infrastructure.Retrieval;
using AiKnowledgeAssistant.Infrastructure.Storage;

var root = Path.Combine(Path.GetTempPath(), "aka-hybrid-中文 空格-" + Guid.NewGuid().ToString("N"));
var failures = new List<string>();
void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
    if (!condition) failures.Add(name);
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
    var fullText = new SqliteDocumentSearch(database);
    var semantic = new SqliteSemanticDocumentSearch(database, new HashingTextEmbedder());
    var hybrid = new HybridDocumentSearch(fullText, semantic);
    var kbA = bases.Create("混合检索库");
    var kbB = bases.Create("隔离库");
    var exactPath = Path.Combine(root, "精确编号.txt");
    var semanticPath = Path.Combine(root, "语义报销.txt");
    var otherPath = Path.Combine(root, "其他库.txt");
    File.WriteAllText(exactPath, "合同编号 ZX-42，需要按 GB 50010 6.2.10 复核。");
    File.WriteAllText(semanticPath, "员工差旅费用需要发票和审批单，财务复核后报销付款。");
    File.WriteAllText(otherPath, "员工差旅费用需要发票，但这是另一个知识库。");
    var exact = importer.Import(kbA.Id, exactPath); Check(parsing.Reparse(exact.Id).ParseStatus == ProcessingStatus.Completed, "精确资料解析完成");
    var semanticDoc = importer.Import(kbA.Id, semanticPath); Check(parsing.Reparse(semanticDoc.Id).ParseStatus == ProcessingStatus.Completed, "语义资料解析完成");
    var other = importer.Import(kbB.Id, otherPath); Check(parsing.Reparse(other.Id).ParseStatus == ProcessingStatus.Completed, "隔离资料解析完成");
    semantic.RebuildIndex();
    Check(semantic.AuditIndex().IsConsistent && fullText.AuditIndex().IsConsistent, "全文与语义索引均一致");
    var numberHits = hybrid.SearchHybrid("GB 50010 6.2.10", [kbA.Id]);
    Check(numberHits.Count > 0 && numberHits[0].DocumentId == exact.Id, "混合检索保留精确编号优先");
    var reimbursementHits = hybrid.SearchHybrid("发票 审批 财务", [kbA.Id]);
    Check(reimbursementHits.Count > 0 && reimbursementHits[0].DocumentId == semanticDoc.Id &&
        reimbursementHits[0].Text.Contains("报销"), "混合检索融合语义候选");
    Check(hybrid.SearchHybrid("发票 审批 财务", [kbB.Id]).All(h => h.DocumentId == other.Id),
        "混合检索严格按知识库隔离");
    using (var db = database.Open())
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM content_embeddings;";
        cmd.ExecuteNonQuery();
    }
    var fallback = hybrid.SearchHybrid("GB 50010 6.2.10", [kbA.Id]);
    Check(fallback.Count > 0 && fallback[0].DocumentId == exact.Id, "语义索引缺失时全文检索仍可用");
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

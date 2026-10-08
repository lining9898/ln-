using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Infrastructure.Database;
using AiKnowledgeAssistant.Infrastructure.Documents;
using AiKnowledgeAssistant.Infrastructure.Embedding;
using AiKnowledgeAssistant.Infrastructure.OCR;
using AiKnowledgeAssistant.Infrastructure.Parser;
using AiKnowledgeAssistant.Infrastructure.Retrieval;
using AiKnowledgeAssistant.Infrastructure.Storage;

var root = Path.Combine(Path.GetTempPath(), "aka-embedding-中文 空格-" + Guid.NewGuid().ToString("N"));
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
    var embedder = new HashingTextEmbedder();
    var semantic = new SqliteSemanticDocumentSearch(database, embedder);
    var kbA = bases.Create("语义资料库");
    var kbB = bases.Create("隔离资料库");
    var refundPath = Path.Combine(root, "报销制度.txt");
    var installPath = Path.Combine(root, "设备安装.txt");
    var otherPath = Path.Combine(root, "其他库.txt");
    File.WriteAllText(refundPath, "公司差旅报销需要发票、审批单和付款记录。财务会在五个工作日内复核。");
    File.WriteAllText(installPath, "设备安装流程包括开箱检查、固定支架、连接电源和完成验收记录。");
    File.WriteAllText(otherPath, "公司差旅报销需要单据，但这个文件属于另一个知识库。");
    var refund = importer.Import(kbA.Id, refundPath); Check(parsing.Reparse(refund.Id).ParseStatus == ProcessingStatus.Completed, "中文 TXT 解析完成");
    var install = importer.Import(kbA.Id, installPath); Check(parsing.Reparse(install.Id).ParseStatus == ProcessingStatus.Completed, "第二篇中文 TXT 解析完成");
    var other = importer.Import(kbB.Id, otherPath); Check(parsing.Reparse(other.Id).ParseStatus == ProcessingStatus.Completed, "隔离库 TXT 解析完成");
    Check(embedder.Dimension == 384 && embedder.EmbedQuery("报销发票").Length == 384, "本地 Embedding 输出 384 维向量");
    semantic.RebuildIndex();
    var audit = semantic.AuditIndex();
    Check(audit.IsConsistent && audit.SourceCount == audit.IndexedCount && audit.IndexedCount >= 3,
        "语义索引覆盖已解析内容且审计一致");
    var reimbursement = semantic.SearchSemantic("发票 审批 财务", [kbA.Id]);
    Check(reimbursement.Count > 0 && reimbursement[0].DocumentId == refund.Id &&
        reimbursement[0].KnowledgeBaseId == kbA.Id && reimbursement[0].Text.Contains("报销"),
        "语义检索命中同库报销资料并保留来源");
    Check(semantic.SearchSemantic("发票 审批 财务", [kbB.Id]).All(h => h.DocumentId == other.Id),
        "语义检索按知识库隔离");
    var installation = semantic.SearchSemantic("支架 电源 验收", [kbA.Id]);
    Check(installation.Count > 0 && installation[0].DocumentId == install.Id, "语义排序优先相关资料");
    using (var db = database.Open())
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM content_embeddings WHERE content_id=(SELECT content_id FROM parsed_content WHERE knowledge_base_id=$kb LIMIT 1);";
        cmd.Parameters.AddWithValue("$kb", kbA.Id.ToString("N"));
        cmd.ExecuteNonQuery();
    }
    Check(semantic.AuditIndex().MissingCount == 1, "语义审计发现缺失向量");
    semantic.RebuildIndex(kbA.Id);
    Check(semantic.AuditIndex().IsConsistent, "按知识库重建语义索引恢复一致性");
    using (var db = database.Open())
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM documents WHERE document_id=$id;";
        cmd.Parameters.AddWithValue("$id", refund.Id.ToString("N"));
        cmd.ExecuteNonQuery();
    }
    Check(semantic.SearchSemantic("发票 审批 财务", [kbA.Id]).All(h => h.DocumentId != refund.Id) &&
        semantic.AuditIndex().IsConsistent, "删除文档后语义向量级联清理");
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

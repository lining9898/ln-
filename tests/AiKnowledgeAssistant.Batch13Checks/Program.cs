using AiKnowledgeAssistant.Core.AI;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Infrastructure.AI;
using AiKnowledgeAssistant.Infrastructure.Database;
using AiKnowledgeAssistant.Infrastructure.Documents;
using AiKnowledgeAssistant.Infrastructure.Embedding;
using AiKnowledgeAssistant.Infrastructure.OCR;
using AiKnowledgeAssistant.Infrastructure.Parser;
using AiKnowledgeAssistant.Infrastructure.Retrieval;
using AiKnowledgeAssistant.Infrastructure.Storage;

var root = Path.Combine(Path.GetTempPath(), "aka-multi-kb-中文 空格-" + Guid.NewGuid().ToString("N"));
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
    var provider = new MultiProvider();
    var rag = new RagAnswerService(hybrid, provider);
    var finance = bases.Create("财务制度");
    var travel = bases.Create("差旅手册");
    var financePath = Path.Combine(root, "财务制度.txt");
    var travelPath = Path.Combine(root, "差旅手册.txt");
    File.WriteAllText(financePath, "报销付款由财务在五个工作日内完成复核。");
    File.WriteAllText(travelPath, "差旅报销材料包括发票、审批单和行程记录。");
    var financeDoc = importer.Import(finance.Id, financePath);
    var travelDoc = importer.Import(travel.Id, travelPath);
    Check(parsing.Reparse(financeDoc.Id).ParseStatus == ProcessingStatus.Completed, "财务库资料解析完成");
    Check(parsing.Reparse(travelDoc.Id).ParseStatus == ProcessingStatus.Completed, "差旅库资料解析完成");
    semantic.RebuildIndex();
    var single = await rag.AnswerAsync("差旅报销材料和财务复核要求是什么？", [finance.Id]);
    Check(single.Citations.All(c => c.Hit.KnowledgeBaseId == finance.Id), "单库问答不越权检索其他库");
    var joined = await rag.AnswerAsync("差旅报销材料和财务复核要求是什么？", [finance.Id, travel.Id]);
    Check(joined.UsedAi && joined.Citations.Select(c => c.Hit.KnowledgeBaseId).Distinct().Count() == 2,
        "多知识库问答保留两个知识库来源");
    Check(provider.LastUserMessage is not null &&
        provider.LastUserMessage.Contains("五个工作日", StringComparison.Ordinal) &&
        provider.LastUserMessage.Contains("发票、审批单", StringComparison.Ordinal),
        "多知识库问答向 AI 发送两个库的必要片段");
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

sealed class MultiProvider : IAiProvider
{
    public string? LastUserMessage { get; private set; }
    public Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        LastUserMessage = request.Messages.Last(m => m.Role == "user").Content;
        return Task.FromResult(new AiChatResponse("材料包括发票、审批单和行程记录；财务五个工作日内复核。[S1][S2]",
            "fake-model", "Fake", DateTimeOffset.UtcNow));
    }
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}

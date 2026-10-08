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

var root = Path.Combine(Path.GetTempPath(), "aka-rag-中文 空格-" + Guid.NewGuid().ToString("N"));
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
    var fake = new FakeProvider();
    var rag = new RagAnswerService(hybrid, fake);
    var kb = bases.Create("RAG 资料库");
    var otherKb = bases.Create("其他资料库");
    var reimbursement = Path.Combine(root, "报销规则.txt");
    var unrelated = Path.Combine(root, "无关资料.txt");
    File.WriteAllText(reimbursement, "差旅报销必须提交发票、审批单和付款记录。财务会在五个工作日内复核。");
    File.WriteAllText(unrelated, "设备安装需要固定支架和电源检查。");
    var reimbursementDoc = importer.Import(kb.Id, reimbursement);
    var unrelatedDoc = importer.Import(otherKb.Id, unrelated);
    Check(parsing.Reparse(reimbursementDoc.Id).ParseStatus == ProcessingStatus.Completed, "RAG 资料解析完成");
    Check(parsing.Reparse(unrelatedDoc.Id).ParseStatus == ProcessingStatus.Completed, "隔离资料解析完成");
    semantic.RebuildIndex();
    var answer = await rag.AnswerAsync("差旅报销需要什么材料？", [kb.Id]);
    Check(answer.UsedAi && answer.Answer.Contains("[S1]", StringComparison.Ordinal) &&
        answer.Citations.Count > 0 && answer.Citations[0].Hit.DocumentId == reimbursementDoc.Id,
        "RAG 使用检索片段生成带来源回答");
    Check(fake.LastUserMessage is not null &&
        fake.LastUserMessage.Contains("差旅报销必须提交发票", StringComparison.Ordinal) &&
        !fake.LastUserMessage.Contains("设备安装需要固定支架", StringComparison.Ordinal),
        "RAG 只发送所选知识库的相关片段");
    var noEvidence = await rag.AnswerAsync("差旅报销需要什么材料？", [Guid.NewGuid()]);
    Check(!noEvidence.UsedAi && noEvidence.Answer == "当前选择的知识库中未检索到足够依据。" &&
        fake.CallCount == 1, "无检索依据时不调用 AI");
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

sealed class FakeProvider : IAiProvider
{
    public int CallCount { get; private set; }
    public string? LastUserMessage { get; private set; }
    public Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastUserMessage = request.Messages.Last(m => m.Role == "user").Content;
        return Task.FromResult(new AiChatResponse("差旅报销需要发票、审批单和付款记录。[S1]", "fake-model", "Fake", DateTimeOffset.UtcNow));
    }
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}

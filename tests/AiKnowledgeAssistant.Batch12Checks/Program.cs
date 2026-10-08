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
using AiKnowledgeAssistant.Infrastructure.Viewer;

var root = Path.Combine(Path.GetTempPath(), "aka-citations-中文 空格-" + Guid.NewGuid().ToString("N"));
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
    var provider = new CitingProvider();
    var rag = new RagAnswerService(hybrid, provider);
    var viewer = new LocalSourceViewer(docs, docs);
    var verifier = new CitationVerifier(viewer);
    var kb = bases.Create("引用核验库");
    var policy = Path.Combine(root, "报销核验.txt");
    File.WriteAllText(policy, "差旅报销必须提交发票、审批单和付款记录。");
    var document = importer.Import(kb.Id, policy);
    Check(parsing.Reparse(document.Id).ParseStatus == ProcessingStatus.Completed, "引用资料解析完成");
    semantic.RebuildIndex();
    var answer = await rag.AnswerAsync("差旅报销需要什么材料？", [kb.Id]);
    var verified = verifier.Verify(answer);
    Check(verified.IsValid && verified.VerifiedCitations.Count == 1 &&
        verified.VerifiedCitations[0].Source.Text.Contains("发票", StringComparison.Ordinal),
        "回答引用可映射回本地原文");
    var forged = answer with { Answer = answer.Answer + " 另见 [S99]" };
    var forgedCheck = verifier.Verify(forged);
    Check(!forgedCheck.IsValid && forgedCheck.MissingSourceIds.SequenceEqual(["S99"]),
        "不存在的来源编号被拒绝");
    var noCitation = answer with { Answer = "差旅报销需要发票、审批单和付款记录。", UsedAi = true };
    Check(!verifier.Verify(noCitation).IsValid, "AI 回答缺少引用时核验失败");
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

sealed class CitingProvider : IAiProvider
{
    public Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AiChatResponse("差旅报销需要发票、审批单和付款记录。[S1]", "fake-model", "Fake", DateTimeOffset.UtcNow));
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}

using System.Net;
using System.Text.Json;
using AiKnowledgeAssistant.Core.AI;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Core.Retrieval;
using AiKnowledgeAssistant.Core.Sources;
using AiKnowledgeAssistant.Infrastructure.AI;

void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
var keys = new Keys();
var handler = new Handler();
var web = new TavilyWebSearch(new HttpClient(handler), keys);
try { await web.SearchAsync("public topic"); throw new Exception("Missing key accepted"); }
catch (WebSearchException) { Check(handler.Calls == 0, "missing search key makes no request"); }
keys.Key = "test-only-key";
var provider = new Provider();
var rag = new RagAnswerService(new Retrieval(), provider, web);
var local = await rag.AnswerAsync("PRIVATE QUESTION", []);
Check(!local.UsedAi && handler.Calls == 0, "local mode makes no web request");
var answer = await rag.AnswerWithWebAsync("PRIVATE QUESTION", [], "public topic");
Check(answer.UsedAi && answer.WebCitations.Count == 1 && answer.Citations.Count == 0, "web evidence works without local results");
Check(handler.Body is not null && !handler.Body.Contains("PRIVATE") && !handler.Body.Contains("test-only-key"), "search receives no private question or key in body");
using (var body = JsonDocument.Parse(handler.Body!))
    Check(body.RootElement.GetProperty("query").GetString() == "public topic" && !body.RootElement.GetProperty("include_raw_content").GetBoolean(), "explicit public query and snippets only");
Check(handler.Authorization == "Bearer test-only-key", "search key in authorization header");
Check(provider.Request!.Messages.Last().Content.Contains("[W1]") && provider.Request.Messages.Last().Content.Contains("https://example.com/source"), "model receives real web source mapping");
var verifier = new CitationVerifier(new Viewer());
var verified = verifier.Verify(answer);
Check(verified.IsValid && verified.WebCitations.Count == 1, "web citation resolves to supplied source");
Check(!verifier.Verify(answer with { Answer = "wrong [W99]" }).IsValid, "invented web citation rejected");
var hit = new SearchHit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "local.pdf", "PRIVATE LOCAL EVIDENCE",
    SourceType.Ocr, 10, null, null, null, null, null, 1);
var mixed = new RagAnswerService(new Retrieval { Hits = [hit] }, provider, web);
provider.Answer = "local evidence [S1], public evidence [W1]";
var combined = await mixed.AnswerWithWebAsync("PRIVATE QUESTION", [hit.KnowledgeBaseId], "public topic");
Check(combined.Citations.Count == 1 && combined.WebCitations.Count == 1 &&
    provider.Request!.Messages.Last().Content.Contains("PRIVATE LOCAL EVIDENCE") && !handler.Body!.Contains("PRIVATE"), "combined local and web context with private text excluded from search");
var checkedCombined = verifier.Verify(combined);
Check(checkedCombined.IsValid && checkedCombined.VerifiedCitations.Single().Source.CurrentPage == 10 &&
    checkedCombined.WebCitations.Count == 1, "combined citations preserve PDF physical page and web URL");
handler.Status = HttpStatusCode.Unauthorized;
var calls = provider.Calls;
try { await rag.AnswerWithWebAsync("question", [], "public topic"); throw new Exception("Unauthorized search accepted"); }
catch (WebSearchException e) { Check(e.Message.Contains("Key") && provider.Calls == calls, "search failure explicit and no generation fallback"); }
handler.Status = HttpStatusCode.OK;
handler.Payload = "{not json";
try { await web.SearchAsync("public topic"); throw new Exception("Malformed JSON accepted"); }
catch (WebSearchException) { Console.WriteLine("PASS malformed response rejected"); }

sealed class Keys : ICredentialStore
{
    public string? Key;
    public string? ReadSecret(string target) => Key;
    public void SaveSecret(string target, string secret) => Key = secret;
    public void DeleteSecret(string target) => Key = null;
}
sealed class Handler : HttpMessageHandler
{
    public int Calls;
    public string? Body;
    public string? Authorization;
    public HttpStatusCode Status = HttpStatusCode.OK;
    public string Payload = """
        {"results":[{"title":"Public source","url":"https://example.com/source","content":"public evidence","published_date":"2026-10-08"},
        {"title":"duplicate","url":"https://example.com/source","content":"duplicate"},
        {"title":"unsafe","url":"file:///C:/private","content":"unsafe"}]}
        """;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++; Body = await request.Content!.ReadAsStringAsync(cancellationToken);
        Authorization = request.Headers.Authorization?.ToString();
        return new HttpResponseMessage(Status) { Content = new StringContent(Payload) };
    }
}
sealed class Retrieval : IHybridDocumentSearch
{
    public IReadOnlyList<SearchHit> Hits = [];
    public IReadOnlyList<SearchHit> SearchHybrid(string query, IReadOnlyCollection<Guid> ids, int limit = 50) => Hits;
}
sealed class Provider : IAiProvider
{
    public int Calls;
    public AiChatRequest? Request;
    public string Answer = "public evidence [W1]";
    public Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    { Calls++; Request = request; return Task.FromResult(new AiChatResponse(Answer, "test", "test", DateTimeOffset.UtcNow)); }
    public Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
}
sealed class Viewer : ISourceViewer
{
    public SourceView Open(SearchHit hit) => new(hit.KnowledgeBaseId, hit.DocumentId, hit.ContentId,
        hit.FileName, "test-only.pdf", "PDF", hit.PageNumber, 58, "physical page", hit.Text);
    public SourceView Open(Guid id, int? pageNumber = null) => throw new InvalidOperationException();
    public SourceView Jump(Guid id, int pageNumber) => throw new InvalidOperationException();
}

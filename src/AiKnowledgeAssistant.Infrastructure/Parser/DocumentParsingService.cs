using AiKnowledgeAssistant.Core.Documents;

namespace AiKnowledgeAssistant.Infrastructure.Parser;

public sealed class DocumentParsingService : IDocumentParsingService
{
    private readonly IDocumentRepository documents;
    private readonly IParsedContentRepository parsed;
    private readonly IReadOnlyList<IDocumentParser> parsers;

    public DocumentParsingService(IDocumentRepository documents, IParsedContentRepository parsed,
        IEnumerable<IDocumentParser> parsers)
    {
        this.documents = documents;
        this.parsed = parsed;
        this.parsers = parsers.ToArray();
    }

    public Document Reparse(Guid documentId)
    {
        var document = documents.Get(documentId)
            ?? throw new InvalidOperationException("文档不存在，请刷新列表。");
        var parser = parsers.SingleOrDefault(p => p.Supports(document.FileType))
            ?? throw new InvalidOperationException("没有可用的文档解析器。");
        parsed.BeginParsing(documentId);
        ParsedDocument result;
        try
        {
            result = parser.Parse(document);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var reason = e is InvalidDataException or System.Xml.XmlException or ArgumentException
                ? "文件内容损坏、为空或与扩展名不匹配。" : "解析器运行失败，请检查文件后重试。";
            result = new ParsedDocument(document.Id, document.KnowledgeBaseId, parser.ParserType,
                null, Array.Empty<ParsedUnit>(), [new ParseFailure(null, reason)], DateTimeOffset.UtcNow);
        }
        if (result.Units.Count == 0 && result.Failures.Count == 0)
            result = result with { Failures = [new ParseFailure(null, "未提取到可用文本。")] };
        parsed.CompleteParsing(documentId, result);
        return documents.Get(documentId)!;
    }
}

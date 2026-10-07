namespace AiKnowledgeAssistant.Core.Documents;

public interface IDocumentParser
{
    string ParserType { get; }
    bool Supports(string fileType);
    ParsedDocument Parse(Document document);
}

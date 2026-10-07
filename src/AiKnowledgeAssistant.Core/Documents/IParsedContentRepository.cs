namespace AiKnowledgeAssistant.Core.Documents;

public interface IParsedContentRepository
{
    ParsedDocument? GetParsed(Guid documentId);
    void BeginParsing(Guid documentId);
    void CompleteParsing(Guid documentId, ParsedDocument result);
    void RecoverInterruptedParsing();
}

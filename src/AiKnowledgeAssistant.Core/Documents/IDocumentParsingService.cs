namespace AiKnowledgeAssistant.Core.Documents;

public interface IDocumentParsingService
{
    Document Reparse(Guid documentId);
}

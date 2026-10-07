namespace AiKnowledgeAssistant.Core.Documents;

public interface IDocumentRepository
{
    IReadOnlyList<Document> List(Guid knowledgeBaseId);
    bool HasDocuments(Guid knowledgeBaseId);
    void Add(Document document);
}

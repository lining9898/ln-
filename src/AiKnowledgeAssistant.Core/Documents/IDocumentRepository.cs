namespace AiKnowledgeAssistant.Core.Documents;

public interface IDocumentRepository
{
    IReadOnlyList<Document> List(Guid knowledgeBaseId);
    Document? Get(Guid documentId);
    bool HasDocuments(Guid knowledgeBaseId);
    void Add(Document document);
}

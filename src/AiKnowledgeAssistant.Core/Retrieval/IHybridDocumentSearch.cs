namespace AiKnowledgeAssistant.Core.Retrieval;

public interface IHybridDocumentSearch
{
    IReadOnlyList<SearchHit> SearchHybrid(string query, IReadOnlyCollection<Guid> knowledgeBaseIds, int limit = 50);
}

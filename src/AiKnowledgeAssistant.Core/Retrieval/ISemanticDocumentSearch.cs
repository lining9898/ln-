namespace AiKnowledgeAssistant.Core.Retrieval;

public sealed record SemanticIndexAudit(int SourceCount, int IndexedCount, int MissingCount,
    int OrphanCount, int DimensionMismatchCount, bool ForeignKeyClean)
{
    public bool IsConsistent => MissingCount == 0 && OrphanCount == 0 &&
        DimensionMismatchCount == 0 && ForeignKeyClean;
}

public interface ISemanticDocumentSearch
{
    IReadOnlyList<SearchHit> SearchSemantic(string query, IReadOnlyCollection<Guid> knowledgeBaseIds, int limit = 50);
    void RebuildIndex(Guid? knowledgeBaseId = null);
    SemanticIndexAudit AuditIndex();
}

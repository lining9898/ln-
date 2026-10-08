using AiKnowledgeAssistant.Core.Documents;

namespace AiKnowledgeAssistant.Core.Retrieval;

public sealed record SearchHit(Guid KnowledgeBaseId, Guid DocumentId, Guid ContentId,
    string FileName, string Text, SourceType SourceType, int? PageNumber,
    string? SectionTitle, string? SectionPath, int? StartLine, int? EndLine,
    int? ParagraphNumber, double Score);

public sealed record SearchIndexAudit(int SourceCount, int IndexedCount, int MissingCount,
    int OrphanCount, int StaleCount, int StatusMismatchCount, bool ForeignKeyClean)
{
    public bool IsConsistent => MissingCount == 0 && OrphanCount == 0 &&
        StaleCount == 0 && StatusMismatchCount == 0 && ForeignKeyClean;
}

public interface IDocumentSearch
{
    IReadOnlyList<SearchHit> Search(string query, IReadOnlyCollection<Guid> knowledgeBaseIds, int limit = 50);
    void RebuildIndex(Guid? knowledgeBaseId = null);
    SearchIndexAudit AuditIndex();
}

using AiKnowledgeAssistant.Core.Retrieval;

namespace AiKnowledgeAssistant.Core.AI;

public sealed record RagCitation(string SourceId, SearchHit Hit);

public sealed record RagAnswer(string Answer, IReadOnlyList<RagCitation> Citations,
    bool UsedAi, string Model)
{
    public IReadOnlyList<WebCitation> WebCitations { get; init; } = [];
}

public interface IRagService
{
    Task<RagAnswer> AnswerAsync(string question, IReadOnlyCollection<Guid> knowledgeBaseIds,
        CancellationToken cancellationToken = default);
    Task<RagAnswer> AnswerWithWebAsync(string question, IReadOnlyCollection<Guid> knowledgeBaseIds,
        string publicQuery, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

using AiKnowledgeAssistant.Core.Retrieval;

namespace AiKnowledgeAssistant.Core.AI;

public sealed record RagCitation(string SourceId, SearchHit Hit);

public sealed record RagAnswer(string Answer, IReadOnlyList<RagCitation> Citations,
    bool UsedAi, string Model);

public interface IRagService
{
    Task<RagAnswer> AnswerAsync(string question, IReadOnlyCollection<Guid> knowledgeBaseIds,
        CancellationToken cancellationToken = default);
}

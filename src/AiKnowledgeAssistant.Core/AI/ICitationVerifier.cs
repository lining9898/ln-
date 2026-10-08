using AiKnowledgeAssistant.Core.Sources;

namespace AiKnowledgeAssistant.Core.AI;

public sealed record VerifiedCitation(string SourceId, SourceView Source);

public sealed record CitationVerification(bool IsValid, IReadOnlyList<string> MissingSourceIds,
    IReadOnlyList<VerifiedCitation> VerifiedCitations);

public interface ICitationVerifier
{
    CitationVerification Verify(RagAnswer answer);
}

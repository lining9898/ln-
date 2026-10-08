namespace AiKnowledgeAssistant.Core.AI;

public sealed record AiMessage(string Role, string Content);

public sealed record AiChatRequest(IReadOnlyList<AiMessage> Messages, int MaxTokens = 1024,
    double Temperature = 0.2);

public sealed record AiChatResponse(string Content, string Model, string Provider,
    DateTimeOffset CreatedAt);

public interface IAiProvider
{
    Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default);
    Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default);
}

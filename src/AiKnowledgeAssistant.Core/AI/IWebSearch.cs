namespace AiKnowledgeAssistant.Core.AI;

public sealed record WebSearchHit(string Title, string Url, string Text, string? PublishedDate);
public sealed record WebCitation(string SourceId, WebSearchHit Hit);
public sealed class WebSearchException(string message) : Exception(message);

public interface IWebSearch
{
    Task<IReadOnlyList<WebSearchHit>> SearchAsync(string publicQuery, CancellationToken cancellationToken = default);
}

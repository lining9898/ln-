using AiKnowledgeAssistant.Core.Retrieval;

namespace AiKnowledgeAssistant.Infrastructure.Retrieval;

public sealed class HybridDocumentSearch(IDocumentSearch fullText, ISemanticDocumentSearch semantic)
    : IHybridDocumentSearch
{
    private const double RrfK = 60d;

    public IReadOnlyList<SearchHit> SearchHybrid(string query, IReadOnlyCollection<Guid> knowledgeBaseIds, int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(knowledgeBaseIds);
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        if (knowledgeBaseIds.Count == 0 || string.IsNullOrWhiteSpace(query)) return [];
        var fullTextHits = fullText.Search(query, knowledgeBaseIds, Math.Min(limit * 4, 200));
        var semanticHits = semantic.SearchSemantic(query, knowledgeBaseIds, Math.Min(limit * 4, 200));
        var merged = new Dictionary<Guid, (SearchHit Hit, double Score, bool FullText, bool Semantic)>();
        Add(fullTextHits, isFullText: true);
        Add(semanticHits, isFullText: false);
        return merged.Values
            .Select(x => x.Hit with { Score = x.Score + (x.FullText && x.Semantic ? 0.05 : 0) })
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.DocumentId)
            .ThenBy(h => h.ContentId)
            .Take(limit)
            .ToArray();

        void Add(IReadOnlyList<SearchHit> hits, bool isFullText)
        {
            for (var i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                var contribution = 1d / (RrfK + i + 1);
                if (isFullText && query.Trim().Length > 0 &&
                    hit.Text.Contains(query.Trim().Trim('"', '“', '”'), StringComparison.OrdinalIgnoreCase))
                    contribution += 0.02;
                if (merged.TryGetValue(hit.ContentId, out var existing))
                {
                    merged[hit.ContentId] = (existing.Hit, existing.Score + contribution,
                        existing.FullText || isFullText, existing.Semantic || !isFullText);
                }
                else
                {
                    merged.Add(hit.ContentId, (hit, contribution, isFullText, !isFullText));
                }
            }
        }
    }
}

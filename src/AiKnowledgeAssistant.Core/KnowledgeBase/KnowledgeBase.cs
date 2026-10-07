using System.Text.Json.Serialization;

namespace AiKnowledgeAssistant.Core.KnowledgeBase;

public sealed record KnowledgeBase(
    [property: JsonPropertyName("knowledge_base_id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

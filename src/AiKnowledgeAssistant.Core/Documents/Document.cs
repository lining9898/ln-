using System.Text.Json.Serialization;

namespace AiKnowledgeAssistant.Core.Documents;

public enum ProcessingStatus { Pending }

public sealed record Document(
    [property: JsonPropertyName("document_id")] Guid Id,
    [property: JsonPropertyName("knowledge_base_id")] Guid KnowledgeBaseId,
    [property: JsonPropertyName("original_file_name")] string OriginalFileName,
    [property: JsonPropertyName("managed_file_path")] string ManagedFilePath,
    [property: JsonPropertyName("file_type")] string FileType,
    [property: JsonPropertyName("file_size")] long FileSize,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("parse_status")] ProcessingStatus ParseStatus,
    [property: JsonPropertyName("index_status")] ProcessingStatus IndexStatus,
    [property: JsonPropertyName("content_hash")] string ContentHash);

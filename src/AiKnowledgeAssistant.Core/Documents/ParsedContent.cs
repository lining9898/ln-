using System.Text.Json.Serialization;

namespace AiKnowledgeAssistant.Core.Documents;

public enum SourceType { Text, Ocr }

public sealed record ParsedUnit(
    [property: JsonPropertyName("document_id")] Guid DocumentId,
    [property: JsonPropertyName("knowledge_base_id")] Guid KnowledgeBaseId,
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("source_type")] SourceType SourceType,
    [property: JsonPropertyName("page_number")] int? PageNumber,
    [property: JsonPropertyName("section_title")] string? SectionTitle,
    [property: JsonPropertyName("section_path")] string? SectionPath,
    [property: JsonPropertyName("start_line")] int? StartLine,
    [property: JsonPropertyName("end_line")] int? EndLine,
    [property: JsonPropertyName("parser_type")] string ParserType,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("paragraph_number")] int? ParagraphNumber = null);

public sealed record ParseFailure(
    [property: JsonPropertyName("page_number")] int? PageNumber,
    [property: JsonPropertyName("reason")] string Reason);

public sealed record ParsedDocument(
    [property: JsonPropertyName("document_id")] Guid DocumentId,
    [property: JsonPropertyName("knowledge_base_id")] Guid KnowledgeBaseId,
    [property: JsonPropertyName("parser_type")] string ParserType,
    [property: JsonPropertyName("page_count")] int? PageCount,
    [property: JsonPropertyName("units")] IReadOnlyList<ParsedUnit> Units,
    [property: JsonPropertyName("failures")] IReadOnlyList<ParseFailure> Failures,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

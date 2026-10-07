namespace AiKnowledgeAssistant.Core.Documents;

public interface IDocumentImporter
{
    Document Import(Guid knowledgeBaseId, string sourcePath);
}

public sealed class DuplicateDocumentException(Document existing)
    : InvalidOperationException($"该知识库已导入相同内容的文件：{existing.OriginalFileName}")
{
    public Guid ExistingDocumentId { get; } = existing.Id;
}

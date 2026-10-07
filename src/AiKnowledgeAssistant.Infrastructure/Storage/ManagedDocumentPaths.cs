using AiKnowledgeAssistant.Core.Storage;

namespace AiKnowledgeAssistant.Infrastructure.Storage;

public sealed class ManagedDocumentPaths(IUserDataPaths paths)
{
    public string DirectoryFor(Guid knowledgeBaseId) =>
        Path.Combine(paths.Documents, knowledgeBaseId.ToString("N"));

    public string FileFor(Guid knowledgeBaseId, Guid documentId, string fileType) =>
        Path.Combine(DirectoryFor(knowledgeBaseId),
            documentId.ToString("N") + "." + fileType.ToLowerInvariant());
}

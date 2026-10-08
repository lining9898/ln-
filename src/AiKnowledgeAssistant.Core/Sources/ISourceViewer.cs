using AiKnowledgeAssistant.Core.Retrieval;

namespace AiKnowledgeAssistant.Core.Sources;

public sealed record SourceView(Guid KnowledgeBaseId, Guid DocumentId, Guid? ContentId,
    string FileName, string ManagedFilePath, string FileType, int? CurrentPage,
    int? PageCount, string Position, string Text);

public interface ISourceViewer
{
    SourceView Open(SearchHit hit);
    SourceView Open(Guid documentId, int? pageNumber = null);
    SourceView Jump(Guid documentId, int pageNumber);
}

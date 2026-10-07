using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Core.KnowledgeBase;
using AiKnowledgeAssistant.Core.Storage;
using AiKnowledgeAssistant.Infrastructure.Storage;

namespace AiKnowledgeAssistant.Infrastructure.Documents;

public sealed class DocumentImportService : IDocumentImporter
{
    private readonly IKnowledgeBaseStore knowledgeBases;
    private readonly IDocumentRepository documents;
    private readonly IFileTransfer transfer;
    private readonly ManagedDocumentPaths paths;

    public DocumentImportService(IKnowledgeBaseStore knowledgeBases, IDocumentRepository documents,
        IUserDataPaths dataPaths, IFileTransfer? transfer = null)
    {
        this.knowledgeBases = knowledgeBases;
        this.documents = documents;
        this.transfer = transfer ?? new LocalFileTransfer();
        paths = new ManagedDocumentPaths(dataPaths);
    }

    public Document Import(Guid knowledgeBaseId, string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("文件不存在，或当前用户无权读取该文件。", sourcePath);
        var name = Path.GetFileName(sourcePath);
        var fileType = Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
        if (fileType is not ("PDF" or "DOCX" or "TXT" or "MD"))
            throw new NotSupportedException("仅支持 PDF、DOCX、TXT 和 MD 文件。");
        return knowledgeBases.ExecuteForExisting(knowledgeBaseId, () => ImportLocked(knowledgeBaseId,
            sourcePath, name, fileType));
    }

    private Document ImportLocked(Guid knowledgeBaseId, string sourcePath, string name, string fileType)
    {
        var documentId = Guid.NewGuid();
        var directory = paths.DirectoryFor(knowledgeBaseId);
        Directory.CreateDirectory(directory);
        var stagingPath = Path.Combine(directory, ".import-" + documentId.ToString("N") + ".tmp");
        var managedPath = paths.FileFor(knowledgeBaseId, documentId, fileType);
        var metadataAttempted = false;
        try
        {
            var (size, hash) = transfer.CopyAndHash(sourcePath, stagingPath);
            if (size == 0) throw new InvalidDataException("不能导入 0 字节文件。");
            var duplicate = documents.List(knowledgeBaseId).FirstOrDefault(d =>
                string.Equals(d.ContentHash, hash, StringComparison.OrdinalIgnoreCase));
            if (duplicate is not null) throw new DuplicateDocumentException(duplicate);
            File.Move(stagingPath, managedPath);
            var now = DateTimeOffset.UtcNow;
            var document = new Document(documentId, knowledgeBaseId, name, managedPath, fileType,
                size, now, now, ProcessingStatus.Pending, ProcessingStatus.Pending, hash);
            metadataAttempted = true;
            documents.Add(document);
            return document;
        }
        catch (Exception failure)
        {
            // After a failed metadata write, read back before removing the copy. If the
            // commit may have succeeded, retain the file to avoid a record without data.
            if (metadataAttempted)
            {
                try
                {
                    var recorded = documents.List(knowledgeBaseId).FirstOrDefault(d => d.Id == documentId);
                    if (recorded is not null) return recorded;
                }
                catch (Exception verificationFailure)
                {
                    throw new IOException("无法确认导入状态；受管理副本已保留，请检查文档元数据后重试。",
                        new AggregateException(failure, verificationFailure));
                }
            }
            if (File.Exists(managedPath)) File.Delete(managedPath);
            throw;
        }
        finally
        {
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
        }
    }
}

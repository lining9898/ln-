using System.Text.Json;
using System.Text.Json.Serialization;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Core.Storage;

namespace AiKnowledgeAssistant.Infrastructure.Storage;

public sealed class JsonDocumentRepository : IDocumentRepository
{
    private readonly string filePath;
    private readonly string lockPath;
    private readonly ManagedDocumentPaths paths;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<ProcessingStatus>(JsonNamingPolicy.SnakeCaseUpper) }
    };

    public JsonDocumentRepository(IUserDataPaths dataPaths)
    {
        filePath = Path.Combine(dataPaths.Databases, "documents.json");
        lockPath = filePath + ".lock";
        paths = new ManagedDocumentPaths(dataPaths);
    }

    private FileStream AcquireLock() => new(lockPath, FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None);

    public IReadOnlyList<Document> List(Guid knowledgeBaseId)
    {
        using var fileLock = AcquireLock();
        return Read().Documents.Where(d => d.KnowledgeBaseId == knowledgeBaseId)
            .OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id).ToArray();
    }

    public bool HasDocuments(Guid knowledgeBaseId)
    {
        using var fileLock = AcquireLock();
        return Read().Documents.Any(d => d.KnowledgeBaseId == knowledgeBaseId);
    }

    public void Add(Document document)
    {
        using var fileLock = AcquireLock();
        var data = Read();
        Validate(document);
        if (data.Documents.Any(d => d.Id == document.Id))
            throw new InvalidOperationException("文档 ID 已存在，请重试导入。");
        var duplicate = data.Documents.FirstOrDefault(d => d.KnowledgeBaseId == document.KnowledgeBaseId &&
            string.Equals(d.ContentHash, document.ContentHash, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null) throw new DuplicateDocumentException(duplicate);
        data.Documents.Add(document);
        Write(data);
    }

    private Snapshot Read()
    {
        if (!File.Exists(filePath)) return new Snapshot();
        try
        {
            using var stream = File.OpenRead(filePath);
            var data = JsonSerializer.Deserialize<Snapshot>(stream, Options);
            if (data is null || data.SchemaVersion != 1 || data.Documents is null)
                throw new InvalidDataException("文档元数据版本不受支持或文件损坏，已停止写入。");
            var ids = new HashSet<Guid>();
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var document in data.Documents)
            {
                Validate(document);
                if (!ids.Add(document.Id) ||
                    !hashes.Add(document.KnowledgeBaseId.ToString("N") + document.ContentHash))
                    throw new InvalidDataException("文档元数据重复，已停止写入。");
            }
            return data;
        }
        catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException("文档元数据无法读取，已停止写入。请保留原文件。", e);
        }
    }

    private void Validate(Document? document)
    {
        if (document is null || document.Id == Guid.Empty || document.KnowledgeBaseId == Guid.Empty ||
            string.IsNullOrWhiteSpace(document.OriginalFileName) ||
            document.OriginalFileName != Path.GetFileName(document.OriginalFileName) ||
            string.IsNullOrWhiteSpace(document.ContentHash) || document.ContentHash.Length != 64 ||
            !document.ContentHash.All(Uri.IsHexDigit) || document.FileSize <= 0 ||
            document.CreatedAt == default || document.UpdatedAt < document.CreatedAt ||
            document.ParseStatus != ProcessingStatus.Pending ||
            document.IndexStatus != ProcessingStatus.Pending ||
            document.FileType is not ("PDF" or "DOCX" or "TXT" or "MD") ||
            !string.Equals(document.ManagedFilePath,
                paths.FileFor(document.KnowledgeBaseId, document.Id, document.FileType),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !File.Exists(document.ManagedFilePath) ||
            new FileInfo(document.ManagedFilePath).Length != document.FileSize)
            throw new InvalidDataException("文档元数据或受管理副本异常，已停止写入。请保留原文件。");
    }

    private void Write(Snapshot data)
    {
        var temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, Options);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed class Snapshot
    {
        [JsonRequired, JsonPropertyName("schema_version")]
        public int SchemaVersion { get; set; } = 1;
        [JsonRequired, JsonPropertyName("documents")]
        public List<Document> Documents { get; set; } = new();
    }
}

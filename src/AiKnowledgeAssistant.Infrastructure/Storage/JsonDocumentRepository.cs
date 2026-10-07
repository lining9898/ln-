using System.Text.Json;
using System.Text.Json.Serialization;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Core.Storage;

namespace AiKnowledgeAssistant.Infrastructure.Storage;

public sealed class JsonDocumentRepository : IDocumentRepository, IParsedContentRepository
{
    private readonly string filePath;
    private readonly string lockPath;
    private readonly ManagedDocumentPaths paths;
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter<ProcessingStatus>(JsonNamingPolicy.SnakeCaseUpper),
            new JsonStringEnumConverter<SourceType>(JsonNamingPolicy.SnakeCaseUpper) }
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

    public Document? Get(Guid documentId)
    {
        using var fileLock = AcquireLock();
        return Read().Documents.FirstOrDefault(d => d.Id == documentId);
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

    public ParsedDocument? GetParsed(Guid documentId)
    {
        using var fileLock = AcquireLock();
        return Read().ParsedDocuments!.FirstOrDefault(p => p.DocumentId == documentId);
    }

    public void BeginParsing(Guid documentId)
    {
        using var fileLock = AcquireLock();
        var data = Read();
        var index = Find(data, documentId);
        if (data.Documents[index].ParseStatus == ProcessingStatus.Parsing)
            throw new InvalidOperationException("此文档已在解析中。");
        data.Documents[index] = data.Documents[index] with
        {
            ParseStatus = ProcessingStatus.Parsing, ParseError = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Write(data);
    }

    public void CompleteParsing(Guid documentId, ParsedDocument result)
    {
        using var fileLock = AcquireLock();
        var data = Read();
        var index = Find(data, documentId);
        var document = data.Documents[index];
        if (document.ParseStatus != ProcessingStatus.Parsing ||
            result.DocumentId != documentId || result.KnowledgeBaseId != document.KnowledgeBaseId)
            throw new InvalidOperationException("解析结果与当前文档或解析状态不一致。");
        ValidateParsed(result, document);
        var status = result.Units.Count == 0 ? ProcessingStatus.Failed :
            result.Failures.Count > 0 ? ProcessingStatus.Partial : ProcessingStatus.Completed;
        var summary = result.Failures.Count > 0
            ? string.Join("；", result.Failures.Take(3).Select(f =>
                f.PageNumber is int page ? $"第 {page} 页：{f.Reason}" : f.Reason))
            : status == ProcessingStatus.Failed ? "未提取到可用文本。" : null;
        data.ParsedDocuments!.RemoveAll(p => p.DocumentId == documentId);
        data.ParsedDocuments.Add(result);
        data.Documents[index] = document with
        {
            ParseStatus = status, ParseError = summary, TotalPages = result.PageCount,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        Write(data);
    }

    public void RecoverInterruptedParsing()
    {
        using var fileLock = AcquireLock();
        var data = Read();
        var changed = false;
        for (var i = 0; i < data.Documents.Count; i++)
        {
            if (data.Documents[i].ParseStatus != ProcessingStatus.Parsing) continue;
            var document = data.Documents[i];
            data.Documents[i] = document with
            {
                ParseStatus = ProcessingStatus.Failed,
                ParseError = "上次解析被中断，请重新解析。",
                UpdatedAt = DateTimeOffset.UtcNow
            };
            data.ParsedDocuments!.RemoveAll(p => p.DocumentId == document.Id);
            changed = true;
        }
        if (changed) Write(data);
    }

    private static int Find(Snapshot data, Guid documentId)
    {
        var index = data.Documents.FindIndex(d => d.Id == documentId);
        if (index < 0) throw new InvalidOperationException("文档不存在，请刷新列表。");
        return index;
    }

    private Snapshot Read()
    {
        if (!File.Exists(filePath)) return new Snapshot { ParsedDocuments = new() };
        try
        {
            using var stream = File.OpenRead(filePath);
            var data = JsonSerializer.Deserialize<Snapshot>(stream, Options);
            if (data is null || data.SchemaVersion is not (1 or 2) || data.Documents is null ||
                (data.SchemaVersion == 2 && data.ParsedDocuments is null))
                throw new InvalidDataException("文档元数据版本不受支持或文件损坏，已停止写入。");
            data.ParsedDocuments ??= new();
            var ids = new HashSet<Guid>();
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var document in data.Documents)
            {
                Validate(document);
                if (!ids.Add(document.Id) ||
                    !hashes.Add(document.KnowledgeBaseId.ToString("N") + document.ContentHash))
                    throw new InvalidDataException("文档元数据重复，已停止写入。");
            }
            var parsedIds = new HashSet<Guid>();
            foreach (var parsed in data.ParsedDocuments)
            {
                var owner = data.Documents.FirstOrDefault(d => d.Id == parsed.DocumentId);
                if (owner is null || !parsedIds.Add(parsed.DocumentId))
                    throw new InvalidDataException("解析结果文档关联异常，已停止写入。");
                ValidateParsed(parsed, owner);
                var expected = parsed.Units.Count == 0 ? ProcessingStatus.Failed :
                    parsed.Failures.Count > 0 ? ProcessingStatus.Partial : ProcessingStatus.Completed;
                if (owner.ParseStatus != ProcessingStatus.Parsing && owner.ParseStatus != expected)
                    throw new InvalidDataException("解析状态与来源数据不一致，已停止写入。");
            }
            if (data.Documents.Any(d => d.ParseStatus is ProcessingStatus.Completed or ProcessingStatus.Partial &&
                !parsedIds.Contains(d.Id)))
                throw new InvalidDataException("已完成文档缺少来源数据，已停止写入。");
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
            !Enum.IsDefined(document.ParseStatus) ||
            document.IndexStatus != ProcessingStatus.Pending ||
            (document.TotalPages is <= 0) ||
            document.FileType is not ("PDF" or "DOCX" or "TXT" or "MD") ||
            !string.Equals(document.ManagedFilePath,
                paths.FileFor(document.KnowledgeBaseId, document.Id, document.FileType),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !File.Exists(document.ManagedFilePath) ||
            new FileInfo(document.ManagedFilePath).Length != document.FileSize)
            throw new InvalidDataException("文档元数据或受管理副本异常，已停止写入。请保留原文件。");
    }

    private static void ValidateParsed(ParsedDocument parsed, Document owner)
    {
        if (parsed.DocumentId != owner.Id || parsed.KnowledgeBaseId != owner.KnowledgeBaseId ||
            parsed.ParserType is not ("PDF" or "DOCX" or "TXT" or "MARKDOWN") ||
            parsed.ParserType != (owner.FileType == "MD" ? "MARKDOWN" : owner.FileType) ||
            parsed.CreatedAt == default || parsed.Units is null || parsed.Failures is null ||
            (parsed.ParserType == "PDF" && parsed.Units.Count > 0 && parsed.PageCount is not > 0) ||
            (parsed.ParserType != "PDF" && parsed.PageCount is not null) ||
            parsed.PageCount is <= 0 ||
            parsed.Failures.Any(f => f is null || string.IsNullOrWhiteSpace(f.Reason) ||
                f.PageNumber is <= 0 ||
                (parsed.PageCount is int count && f.PageNumber > count)))
            throw new InvalidDataException("解析结果结构无效，已停止写入。");
        for (var i = 0; i < parsed.Units.Count; i++)
        {
            var unit = parsed.Units[i];
            if (unit is null || unit.DocumentId != owner.Id ||
                unit.KnowledgeBaseId != owner.KnowledgeBaseId || unit.Sequence != i + 1 ||
                string.IsNullOrWhiteSpace(unit.Text) || !Enum.IsDefined(unit.SourceType) ||
                (unit.SourceType == SourceType.Ocr && parsed.ParserType != "PDF") ||
                unit.ParserType != parsed.ParserType || unit.CreatedAt == default ||
                (parsed.ParserType == "PDF" &&
                    (unit.PageNumber is null or <= 0 || unit.PageNumber > parsed.PageCount)) ||
                (parsed.ParserType != "PDF" && unit.PageNumber is not null) ||
                (parsed.ParserType == "DOCX" &&
                    (unit.ParagraphNumber is null or <= 0 || unit.StartLine is not null ||
                        unit.EndLine is not null)) ||
                (parsed.ParserType != "DOCX" && unit.ParagraphNumber is not null) ||
                (parsed.ParserType is "TXT" or "MARKDOWN") &&
                    (unit.StartLine is null or <= 0 || unit.EndLine < unit.StartLine))
                throw new InvalidDataException("解析文本来源位置无效，已停止写入。");
        }
    }

    private void Write(Snapshot data)
    {
        data.SchemaVersion = 2;
        data.ParsedDocuments ??= new();
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
        public int SchemaVersion { get; set; } = 2;
        [JsonRequired, JsonPropertyName("documents")]
        public List<Document> Documents { get; set; } = new();
        [JsonPropertyName("parsed_documents")]
        public List<ParsedDocument>? ParsedDocuments { get; set; }
    }
}

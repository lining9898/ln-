using System.Text.Json;
using System.Text.Json.Serialization;
using AiKnowledgeAssistant.Core.KnowledgeBase;
using AiKnowledgeAssistant.Core.Storage;

namespace AiKnowledgeAssistant.Infrastructure.Storage;

public sealed class JsonKnowledgeBaseStore : IKnowledgeBaseStore
{
    private readonly string filePath;
    private readonly string lockPath;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonKnowledgeBaseStore(IUserDataPaths paths)
    {
        filePath = Path.Combine(paths.Databases, "knowledge-bases.json");
        lockPath = filePath + ".lock";
    }

    // A per-file exclusive lock serializes read-modify-write across instances/processes.
    // Failure to acquire is reported; never write based on an unlocked stale snapshot.
    private FileStream AcquireLock() => new(lockPath, FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.None);

    public IReadOnlyList<KnowledgeBase> List()
    {
        using var fileLock = AcquireLock();
        return Read().KnowledgeBases.OrderBy(k => k.CreatedAt).ThenBy(k => k.Id).ToArray();
    }

    public KnowledgeBase Create(string name)
    {
        name = KnowledgeBaseName.Normalize(name);
        using var fileLock = AcquireLock();
        var data = Read();
        EnsureUnique(data, name);
        var now = DateTimeOffset.UtcNow;
        var item = new KnowledgeBase(Guid.NewGuid(), name, now, now);
        data.KnowledgeBases.Add(item);
        Write(data);
        return item;
    }

    public KnowledgeBase Rename(Guid id, string name)
    {
        name = KnowledgeBaseName.Normalize(name);
        using var fileLock = AcquireLock();
        var data = Read();
        var index = Find(data, id);
        EnsureUnique(data, name, id);
        var item = data.KnowledgeBases[index];
        if (item.Name == name) return item;
        item = item with { Name = name, UpdatedAt = DateTimeOffset.UtcNow };
        data.KnowledgeBases[index] = item;
        Write(data);
        return item;
    }

    public void Delete(Guid id)
    {
        using var fileLock = AcquireLock();
        var data = Read();
        data.KnowledgeBases.RemoveAt(Find(data, id));
        // BATCH 2 has no document data; do not recursively delete any directories.
        Write(data);
    }

    private static int Find(Snapshot data, Guid id)
    {
        var index = data.KnowledgeBases.FindIndex(k => k.Id == id);
        if (index < 0) throw new InvalidOperationException("该知识库已不存在，请刷新列表。");
        return index;
    }

    private static void EnsureUnique(Snapshot data, string name, Guid? except = null)
    {
        if (data.KnowledgeBases.Any(k => k.Id != except &&
            string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("已存在同名知识库，请使用其他名称。");
    }

    private Snapshot Read()
    {
        if (!File.Exists(filePath)) return new Snapshot();
        try
        {
            using var stream = File.OpenRead(filePath);
            var data = JsonSerializer.Deserialize<Snapshot>(stream, Options);
            if (data is null || data.SchemaVersion != 1 || data.KnowledgeBases is null)
                throw new InvalidDataException("知识库数据版本不受支持或文件损坏，已停止写入。请保留原文件。");
            var ids = new HashSet<Guid>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in data.KnowledgeBases)
            {
                if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) ||
                    item.Name != KnowledgeBaseName.Normalize(item.Name) || !names.Add(item.Name) ||
                    item.CreatedAt == default || item.UpdatedAt < item.CreatedAt)
                    throw new InvalidDataException("知识库元数据异常，已停止写入。请保留原文件。");
            }
            return data;
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw new InvalidDataException("知识库数据无法读取，已停止写入。请保留原文件。", e);
        }
    }

    private void Write(Snapshot data)
    {
        // Same-directory temporary file, flush, then replace; failed writes retain old snapshot.
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
        public int SchemaVersion { get; set; }
        [JsonRequired, JsonPropertyName("knowledge_bases")]
        public List<KnowledgeBase> KnowledgeBases { get; set; }
        public Snapshot()
        {
            SchemaVersion = 1;
            KnowledgeBases = new();
        }
    }
}

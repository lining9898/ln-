using AiKnowledgeAssistant.Infrastructure.Storage;

namespace AiKnowledgeAssistant.Infrastructure.Database;

// The JSON repositories are read only in this one-time migration path.
public sealed class LegacyJsonMigration(SqliteDatabase database)
{
    public void MigrateIfNeeded()
    {
        using var migrationLock = SqliteDatabase.WriteLock(database.LockPath);
        using var db = database.Open();
        if (SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM app_meta WHERE key='legacy_json_migrated';") != 0) return;
        if (SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM knowledge_bases;") != 0 ||
            SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM documents;") != 0 ||
            SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM parsed_content;") != 0)
            throw new InvalidDataException("SQLite 已有数据但缺少迁移标记，已停止自动迁移，请保留数据库。");
        var kbFile = Path.Combine(database.Paths.Databases,"knowledge-bases.json");
        var docFile = Path.Combine(database.Paths.Databases,"documents.json");
        if (File.Exists(docFile) && !File.Exists(kbFile))
            throw new InvalidDataException("找到旧文档数据但缺少知识库数据，迁移已取消，旧文件保持不变。");
        var knowledgeBases = File.Exists(kbFile) ? new JsonKnowledgeBaseStore(database.Paths).List() : [];
        var legacyDocuments = new JsonDocumentRepository(database.Paths);
        var documents = knowledgeBases.SelectMany(k => legacyDocuments.List(k.Id)).ToArray();
        if (File.Exists(docFile))
        {
            using var raw = System.Text.Json.JsonDocument.Parse(File.ReadAllText(docFile));
            var rawCount = raw.RootElement.GetProperty("documents").GetArrayLength();
            if (rawCount != documents.Length)
                throw new InvalidDataException("旧文档存在孤立知识库关联，迁移已取消，旧文件保持不变。");
        }
        var parsed = documents.Select(d => legacyDocuments.GetParsed(d.Id)).Where(p => p is not null).ToArray();
        using var tx = db.BeginTransaction();
        foreach (var kb in knowledgeBases) SqliteKnowledgeBaseStore.Insert(db,tx,kb);
        foreach (var document in documents) SqliteDocumentRepository.Insert(db,tx,document);
        foreach (var item in parsed) SqliteDocumentRepository.InsertParsed(db,tx,item!);
        if (SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM knowledge_bases;",tx) != knowledgeBases.Count ||
            SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM documents;",tx) != documents.Length ||
            SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM parsed_content;",tx) != parsed.Sum(p=>p!.Units.Count) ||
            SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM pragma_foreign_key_check;",tx) != 0)
            throw new InvalidDataException("迁移校验失败，SQLite 已回滚；旧 JSON 保留。");
        SqliteDatabase.Execute(db,tx,"INSERT INTO app_meta(key,value) VALUES('legacy_json_migrated','1');");
        tx.Commit();
    }
}

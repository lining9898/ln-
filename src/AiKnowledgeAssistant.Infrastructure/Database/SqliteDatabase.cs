using AiKnowledgeAssistant.Core.Storage;
using Microsoft.Data.Sqlite;
using AiKnowledgeAssistant.Infrastructure.Retrieval;

namespace AiKnowledgeAssistant.Infrastructure.Database;

public sealed record DatabaseAudit(int KnowledgeBaseCount, int DocumentCount, int ParsedContentCount,
    int OrphanDocumentCount, int OrphanContentCount, bool ForeignKeyClean);

public sealed class SqliteDatabase
{
    public const int CurrentSchemaVersion = 2;
    public string FilePath { get; }
    internal IUserDataPaths Paths { get; }
    internal string LockPath => FilePath + ".write.lock";

    public SqliteDatabase(IUserDataPaths paths)
    {
        Paths = paths;
        FilePath = Path.Combine(paths.Databases, "knowledge.db");
    }

    public void Initialize()
    {
        Directory.CreateDirectory(Paths.Databases);
        using var initializationLock = WriteLock(LockPath);
        try
        {
            using var db = Open();
            if (ScalarString(db, "PRAGMA integrity_check;") != "ok")
                throw new InvalidDataException("SQLite 数据库完整性检查失败，请保留原数据库并从备份恢复。");
            using (var command = db.CreateCommand())
            {
                command.CommandText = "PRAGMA journal_mode=WAL;";
                command.ExecuteNonQuery();
            }
            var hasVersion = ScalarLong(db, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='schema_version';") == 1;
            var version = 0L;
            if (hasVersion)
            {
                if (ScalarLong(db, "SELECT count(*) FROM schema_version;") != 1)
                    throw new InvalidDataException("SQLite 数据库 Schema 版本异常，请保留原数据库。");
                version = ScalarLong(db, "SELECT version FROM schema_version LIMIT 1;");
                if (version is < 1 or > CurrentSchemaVersion)
                    throw new InvalidDataException("SQLite 数据库 Schema 版本不受支持，请保留原数据库。");
            }
            else
            {
                if (ScalarLong(db, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';") != 0)
                    throw new InvalidDataException("SQLite 数据库结构未知，已停止写入，请保留原数据库。");
                using var transaction = db.BeginTransaction();
                Execute(db, transaction, SchemaV1);
                Execute(db, transaction, "INSERT INTO schema_version(version) VALUES (1);");
                transaction.Commit();
                version = 1;
            }
            if (version == 1)
            {
                using var transaction = db.BeginTransaction();
                Execute(db, transaction, SchemaV2);
                using (var command = db.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "SELECT rowid,content_id,knowledge_base_id,text FROM parsed_content ORDER BY rowid;";
                    var rows = new List<(long RowId, string Id, string Kb, string Text)>();
                    using (var reader = command.ExecuteReader())
                        while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
                    foreach (var row in rows) SqliteDocumentSearch.Insert(db, transaction, row.RowId, row.Id, row.Kb, row.Text);
                }
                Execute(db, transaction, "UPDATE documents SET index_status='COMPLETED' WHERE parse_status IN ('COMPLETED','PARTIAL');");
                Execute(db, transaction, "UPDATE schema_version SET version=2;");
                transaction.Commit();
            }
            if (!Audit().ForeignKeyClean)
                throw new InvalidDataException("SQLite 外键检查失败，请保留原数据库。");
        }
        catch (SqliteException e)
        {
            throw new InvalidDataException("SQLite 数据库无法打开或写入，请检查磁盘与权限，原数据库已保留。", e);
        }
    }

    public SqliteConnection Open()
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = FilePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        try
        {
            db.Open();
            using var command = db.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            command.ExecuteNonQuery();
            if (ScalarLong(db, "PRAGMA foreign_keys;") != 1)
                throw new InvalidDataException("SQLite 外键未启用，已停止写入。");
            return db;
        }
        catch { db.Dispose(); throw; }
    }

    public DatabaseAudit Audit()
    {
        using var db = Open();
        return new DatabaseAudit((int)ScalarLong(db, "SELECT count(*) FROM knowledge_bases;"),
            (int)ScalarLong(db, "SELECT count(*) FROM documents;"),
            (int)ScalarLong(db, "SELECT count(*) FROM parsed_content;"),
            (int)ScalarLong(db, "SELECT count(*) FROM documents d LEFT JOIN knowledge_bases k ON k.knowledge_base_id=d.knowledge_base_id WHERE k.knowledge_base_id IS NULL;"),
            (int)ScalarLong(db, "SELECT count(*) FROM parsed_content c LEFT JOIN documents d ON d.document_id=c.document_id AND d.knowledge_base_id=c.knowledge_base_id WHERE d.document_id IS NULL;"),
            ScalarLong(db, "SELECT count(*) FROM pragma_foreign_key_check;") == 0);
    }

    public (string Version, bool Fts5Available) ProbeCapabilities()
    {
        using var db = Open();
        var version = ScalarString(db, "SELECT sqlite_version();");
        try
        {
            Execute(db, null, "CREATE VIRTUAL TABLE temp.fts5_probe USING fts5(body); INSERT INTO temp.fts5_probe(body) VALUES ('中文 检索');");
            var found = ScalarLong(db, "SELECT count(*) FROM temp.fts5_probe WHERE fts5_probe MATCH '检索';") == 1;
            Execute(db, null, "DROP TABLE temp.fts5_probe;");
            return (version, found);
        }
        catch (SqliteException) { return (version, false); }
    }

    internal static long ScalarLong(SqliteConnection db, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
    internal static string ScalarString(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar()) ?? "";
    }
    internal static void Execute(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] values)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
    internal static FileStream WriteLock(string path) => new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private const string SchemaV2 = """
        CREATE VIRTUAL TABLE content_fts USING fts5(content_id UNINDEXED, knowledge_base_id UNINDEXED, terms, tokenize='unicode61 remove_diacritics 2');
        CREATE TRIGGER parsed_content_fts_delete AFTER DELETE ON parsed_content BEGIN
          DELETE FROM content_fts WHERE rowid=old.rowid;
        END;
        CREATE TRIGGER parsed_content_fts_text_update AFTER UPDATE OF text ON parsed_content BEGIN
          DELETE FROM content_fts WHERE rowid=old.rowid;
          UPDATE documents SET index_status='PENDING' WHERE document_id=old.document_id;
        END;
        """;

    private const string SchemaV1 = """
        CREATE TABLE schema_version(version INTEGER NOT NULL);
        CREATE TABLE app_meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE knowledge_bases(
          knowledge_base_id TEXT PRIMARY KEY, name TEXT NOT NULL COLLATE NOCASE UNIQUE,
          created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
        CREATE TABLE documents(
          document_id TEXT PRIMARY KEY, knowledge_base_id TEXT NOT NULL,
          original_file_name TEXT NOT NULL, managed_file_path TEXT NOT NULL,
          file_type TEXT NOT NULL, file_size INTEGER NOT NULL CHECK(file_size>0),
          created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
          parse_status TEXT NOT NULL, index_status TEXT NOT NULL,
          content_hash TEXT NOT NULL, parse_error TEXT, total_pages INTEGER,
          FOREIGN KEY(knowledge_base_id) REFERENCES knowledge_bases(knowledge_base_id) ON DELETE RESTRICT,
          UNIQUE(knowledge_base_id, document_id), UNIQUE(knowledge_base_id, content_hash));
        CREATE INDEX documents_by_kb ON documents(knowledge_base_id);
        CREATE TABLE parsed_documents(
          document_id TEXT PRIMARY KEY, knowledge_base_id TEXT NOT NULL,
          parser_type TEXT NOT NULL, page_count INTEGER, created_at TEXT NOT NULL,
          FOREIGN KEY(knowledge_base_id, document_id) REFERENCES documents(knowledge_base_id, document_id) ON DELETE CASCADE,
          UNIQUE(knowledge_base_id, document_id));
        CREATE TABLE parsed_content(
          content_id TEXT PRIMARY KEY, document_id TEXT NOT NULL, knowledge_base_id TEXT NOT NULL,
          sequence INTEGER NOT NULL, text TEXT NOT NULL, source_type TEXT NOT NULL,
          page_number INTEGER, section_title TEXT, section_path TEXT,
          start_line INTEGER, end_line INTEGER, paragraph_number INTEGER,
          parser_type TEXT NOT NULL, created_at TEXT NOT NULL,
          FOREIGN KEY(knowledge_base_id, document_id) REFERENCES parsed_documents(knowledge_base_id, document_id) ON DELETE CASCADE,
          UNIQUE(document_id, sequence));
        CREATE TABLE parse_failures(
          failure_id TEXT PRIMARY KEY, document_id TEXT NOT NULL, knowledge_base_id TEXT NOT NULL,
          page_number INTEGER, reason TEXT NOT NULL,
          FOREIGN KEY(knowledge_base_id, document_id) REFERENCES parsed_documents(knowledge_base_id, document_id) ON DELETE CASCADE);
        """;
}

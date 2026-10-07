using AiKnowledgeAssistant.Core.KnowledgeBase;
using Microsoft.Data.Sqlite;

namespace AiKnowledgeAssistant.Infrastructure.Database;

public sealed class SqliteKnowledgeBaseStore(SqliteDatabase database) : IKnowledgeBaseStore
{
    public IReadOnlyList<KnowledgeBase> List()
    {
        using var db = database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT knowledge_base_id,name,created_at,updated_at FROM knowledge_bases ORDER BY created_at,knowledge_base_id;";
        using var reader = cmd.ExecuteReader();
        var items = new List<KnowledgeBase>();
        while (reader.Read()) items.Add(Read(reader));
        return items;
    }

    public KnowledgeBase Create(string name)
    {
        name = KnowledgeBaseName.Normalize(name);
        var now = DateTimeOffset.UtcNow;
        var item = new KnowledgeBase(Guid.NewGuid(), name, now, now);
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        try
        {
            Insert(db, tx, item); tx.Commit(); return item;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        { throw new ArgumentException("已存在同名知识库，请使用其他名称。", e); }
    }

    public KnowledgeBase Rename(Guid id, string name)
    {
        name = KnowledgeBaseName.Normalize(name);
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        var item = Find(db, tx, id) ?? throw new InvalidOperationException("该知识库已不存在，请刷新列表。");
        if (item.Name == name) return item;
        item = item with { Name = name, UpdatedAt = DateTimeOffset.UtcNow };
        try
        {
            SqliteDatabase.Execute(db, tx, "UPDATE knowledge_bases SET name=$name,updated_at=$updated WHERE knowledge_base_id=$id;",
                ("$name", item.Name), ("$updated", item.UpdatedAt.ToString("O")), ("$id", id.ToString("N")));
            tx.Commit(); return item;
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        { throw new ArgumentException("已存在同名知识库，请使用其他名称。", e); }
    }

    public void Delete(Guid id)
    {
        using var fileLock = SqliteDatabase.WriteLock(database.LockPath);
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        if (Find(db, tx, id) is null) throw new InvalidOperationException("该知识库已不存在，请刷新列表。");
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT count(*) FROM documents WHERE knowledge_base_id=$id;";
        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        if (Convert.ToInt64(cmd.ExecuteScalar()) != 0)
            throw new InvalidOperationException("该知识库已有文件。当前批次暂不支持删除非空知识库，以避免丢失资料。");
        SqliteDatabase.Execute(db, tx, "DELETE FROM knowledge_bases WHERE knowledge_base_id=$id;", ("$id", id.ToString("N")));
        tx.Commit();
    }

    public T ExecuteForExisting<T>(Guid id, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var fileLock = SqliteDatabase.WriteLock(database.LockPath);
        using (var db = database.Open())
            if (Find(db, null, id) is null) throw new InvalidOperationException("该知识库已不存在，请刷新列表。");
        return action();
    }

    internal static void Insert(SqliteConnection db, SqliteTransaction tx, KnowledgeBase item) =>
        SqliteDatabase.Execute(db, tx, "INSERT INTO knowledge_bases VALUES($id,$name,$created,$updated);",
            ("$id", item.Id.ToString("N")), ("$name", item.Name),
            ("$created", item.CreatedAt.ToString("O")), ("$updated", item.UpdatedAt.ToString("O")));

    private static KnowledgeBase? Find(SqliteConnection db, SqliteTransaction? tx, Guid id)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT knowledge_base_id,name,created_at,updated_at FROM knowledge_bases WHERE knowledge_base_id=$id;";
        cmd.Parameters.AddWithValue("$id", id.ToString("N"));
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }
    private static KnowledgeBase Read(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)), r.GetString(1),
        DateTimeOffset.Parse(r.GetString(2)), DateTimeOffset.Parse(r.GetString(3)));
}

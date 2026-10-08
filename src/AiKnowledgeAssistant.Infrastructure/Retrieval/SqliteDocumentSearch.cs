using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Core.Retrieval;
using AiKnowledgeAssistant.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace AiKnowledgeAssistant.Infrastructure.Retrieval;

public sealed class SqliteDocumentSearch(SqliteDatabase database) : IDocumentSearch
{
    public IReadOnlyList<SearchHit> Search(string query, IReadOnlyCollection<Guid> knowledgeBaseIds, int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(knowledgeBaseIds);
        if (query.Length > 300) throw new ArgumentException("搜索词不能超过 300 个字符。");
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        if (knowledgeBaseIds.Count == 0) return [];
        var match = SearchTerms.MatchExpression(query);
        if (match.Length == 0) return [];
        var phrases = SearchTerms.ExactPhrases(query);
        using var db = database.Open();
        using var cmd = db.CreateCommand();
        var ids = knowledgeBaseIds.Distinct().ToArray();
        var filters = new List<string>();
        for (var i = 0; i < ids.Length; i++)
        {
            var key = "$kb" + i;
            filters.Add(key); cmd.Parameters.AddWithValue(key, ids[i].ToString("N"));
        }
        cmd.CommandText = $"""
            SELECT c.knowledge_base_id,c.document_id,c.content_id,d.original_file_name,
                   c.text,c.source_type,c.page_number,c.section_title,c.section_path,
                   c.start_line,c.end_line,c.paragraph_number,bm25(content_fts) AS rank
            FROM content_fts
            JOIN parsed_content c ON c.rowid=content_fts.rowid AND c.content_id=content_fts.content_id AND c.knowledge_base_id=content_fts.knowledge_base_id
            JOIN documents d ON d.document_id=c.document_id AND d.knowledge_base_id=c.knowledge_base_id
            WHERE content_fts MATCH $match AND c.knowledge_base_id IN ({string.Join(',', filters)})
              AND d.index_status='COMPLETED' AND d.parse_status IN ('COMPLETED','PARTIAL')
            ORDER BY rank, c.document_id, c.sequence LIMIT $candidateLimit;
            """;
        cmd.Parameters.AddWithValue("$match", match);
        cmd.Parameters.AddWithValue("$candidateLimit", Math.Min(limit * 20, 2000));
        using var reader = cmd.ExecuteReader();
        var hits = new List<SearchHit>();
        while (reader.Read())
        {
            var original = reader.GetString(4);
            if (phrases.Any(p => !original.Contains(p, StringComparison.OrdinalIgnoreCase))) continue;
            var score = -reader.GetDouble(12);
            // Prefer contiguous query text and retain FTS BM25 as the base score.
            var plainQuery = query.Trim().Trim('"', '“', '”');
            if (plainQuery.Length > 0 && original.Contains(plainQuery, StringComparison.OrdinalIgnoreCase)) score += 10;
            hits.Add(new SearchHit(Guid.Parse(reader.GetString(0)),Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),reader.GetString(3),original,
                Enum.Parse<SourceType>(reader.GetString(5),true),NullableInt(reader,6),
                NullableString(reader,7),NullableString(reader,8),NullableInt(reader,9),
                NullableInt(reader,10),NullableInt(reader,11),score));
        }
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.DocumentId)
            .ThenBy(h => h.ContentId).Take(limit).ToArray();
    }

    public void RebuildIndex(Guid? knowledgeBaseId = null)
    {
        using var db = database.Open(); using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT c.rowid,c.content_id,c.knowledge_base_id,c.text FROM parsed_content c WHERE $kb IS NULL OR c.knowledge_base_id=$kb ORDER BY c.rowid;";
        cmd.Parameters.AddWithValue("$kb", knowledgeBaseId?.ToString("N") ?? (object)DBNull.Value);
        var contents = new List<(long RowId,string Id,string Kb,string Text)>();
        using (var r = cmd.ExecuteReader())
            while (r.Read()) contents.Add((r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3)));
        SqliteDatabase.Execute(db,tx,"DELETE FROM content_fts WHERE $kb IS NULL OR knowledge_base_id=$kb;",
            ("$kb",knowledgeBaseId?.ToString("N")));
        foreach (var item in contents) Insert(db,tx,item.RowId,item.Id,item.Kb,item.Text);
        SqliteDatabase.Execute(db,tx,"UPDATE documents SET index_status=CASE WHEN parse_status IN ('COMPLETED','PARTIAL') THEN 'COMPLETED' ELSE 'PENDING' END WHERE $kb IS NULL OR knowledge_base_id=$kb;",
            ("$kb",knowledgeBaseId?.ToString("N")));
        tx.Commit();
    }

    public SearchIndexAudit AuditIndex()
    {
        using var db = database.Open();
        var source = (int)SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM parsed_content;");
        var indexed = (int)SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM content_fts;");
        var missing = (int)SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM parsed_content c LEFT JOIN content_fts f ON f.rowid=c.rowid AND f.content_id=c.content_id AND f.knowledge_base_id=c.knowledge_base_id WHERE f.rowid IS NULL;");
        var orphan = (int)SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM content_fts f LEFT JOIN parsed_content c ON c.rowid=f.rowid AND c.content_id=f.content_id AND c.knowledge_base_id=f.knowledge_base_id WHERE c.rowid IS NULL;");
        var stale = 0;
        using (var cmd=db.CreateCommand())
        {
            cmd.CommandText="SELECT c.text,f.terms FROM parsed_content c JOIN content_fts f ON f.rowid=c.rowid AND f.content_id=c.content_id AND f.knowledge_base_id=c.knowledge_base_id;";
            using var r=cmd.ExecuteReader();
            while (r.Read()) if (r.GetString(1)!=SearchTerms.Build(r.GetString(0))) stale++;
        }
        var statusMismatch = (int)SqliteDatabase.ScalarLong(db,"""
            SELECT count(*) FROM documents d WHERE
              (d.parse_status IN ('COMPLETED','PARTIAL') AND d.index_status!='COMPLETED') OR
              (d.index_status='COMPLETED' AND d.parse_status NOT IN ('COMPLETED','PARTIAL'));
            """);
        var clean = SqliteDatabase.ScalarLong(db,"SELECT count(*) FROM pragma_foreign_key_check;")==0;
        return new SearchIndexAudit(source,indexed,missing,orphan,stale,statusMismatch,clean);
    }

    internal static void Insert(SqliteConnection db, SqliteTransaction tx, long rowId, string contentId, string knowledgeBaseId, string text) =>
        SqliteDatabase.Execute(db,tx,"INSERT INTO content_fts(rowid,content_id,knowledge_base_id,terms) VALUES($row,$id,$kb,$terms);",
            ("$row",rowId),("$id",contentId),("$kb",knowledgeBaseId),("$terms",SearchTerms.Build(text)));
    private static int? NullableInt(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetInt32(i);
    private static string? NullableString(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
}

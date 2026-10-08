using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Core.Embedding;
using AiKnowledgeAssistant.Core.Retrieval;
using AiKnowledgeAssistant.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace AiKnowledgeAssistant.Infrastructure.Retrieval;

public sealed class SqliteSemanticDocumentSearch(SqliteDatabase database, ITextEmbedder embedder)
    : ISemanticDocumentSearch
{
    private const int ChunkVersion = 1;

    public IReadOnlyList<SearchHit> SearchSemantic(string query, IReadOnlyCollection<Guid> knowledgeBaseIds, int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(knowledgeBaseIds);
        if (query.Length > 300) throw new ArgumentException("搜索词不能超过 300 个字符。");
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        if (knowledgeBaseIds.Count == 0 || string.IsNullOrWhiteSpace(query)) return [];
        EnsureSchema();
        var queryVector = embedder.EmbedQuery(query);
        using var db = database.Open();
        using var cmd = db.CreateCommand();
        var ids = knowledgeBaseIds.Distinct().ToArray();
        var filters = new List<string>();
        for (var i = 0; i < ids.Length; i++)
        {
            var key = "$kb" + i;
            filters.Add(key);
            cmd.Parameters.AddWithValue(key, ids[i].ToString("N"));
        }
        cmd.CommandText = $"""
            SELECT c.knowledge_base_id,c.document_id,c.content_id,d.original_file_name,
                   c.text,c.source_type,c.page_number,c.section_title,c.section_path,
                   c.start_line,c.end_line,c.paragraph_number,e.vector
            FROM content_embeddings e
            JOIN parsed_content c ON c.content_id=e.content_id AND c.knowledge_base_id=e.knowledge_base_id
            JOIN documents d ON d.document_id=c.document_id AND d.knowledge_base_id=c.knowledge_base_id
            WHERE e.model_id=$model AND e.chunk_version=$chunk AND e.dimension=$dimension
              AND c.knowledge_base_id IN ({string.Join(',', filters)})
              AND d.parse_status IN ('COMPLETED','PARTIAL')
            ORDER BY c.rowid;
            """;
        cmd.Parameters.AddWithValue("$model", embedder.ModelId);
        cmd.Parameters.AddWithValue("$chunk", ChunkVersion);
        cmd.Parameters.AddWithValue("$dimension", embedder.Dimension);
        using var reader = cmd.ExecuteReader();
        var hits = new List<SearchHit>();
        while (reader.Read())
        {
            var vector = ReadVector((byte[])reader["vector"], embedder.Dimension);
            var score = Dot(queryVector, vector);
            if (score <= 0) continue;
            hits.Add(new SearchHit(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)), reader.GetString(3), reader.GetString(4),
                Enum.Parse<SourceType>(reader.GetString(5), true), NullableInt(reader, 6),
                NullableString(reader, 7), NullableString(reader, 8), NullableInt(reader, 9),
                NullableInt(reader, 10), NullableInt(reader, 11), score));
        }
        return hits.OrderByDescending(h => h.Score).ThenBy(h => h.DocumentId)
            .ThenBy(h => h.ContentId).Take(limit).ToArray();
    }

    public void RebuildIndex(Guid? knowledgeBaseId = null)
    {
        EnsureSchema();
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        SqliteDatabase.Execute(db, tx,
            "DELETE FROM content_embeddings WHERE model_id=$model AND ($kb IS NULL OR knowledge_base_id=$kb);",
            ("$model", embedder.ModelId), ("$kb", knowledgeBaseId?.ToString("N")));
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT c.content_id,c.document_id,c.knowledge_base_id,c.text
            FROM parsed_content c
            JOIN documents d ON d.document_id=c.document_id AND d.knowledge_base_id=c.knowledge_base_id
            WHERE ($kb IS NULL OR c.knowledge_base_id=$kb)
              AND d.parse_status IN ('COMPLETED','PARTIAL')
            ORDER BY c.rowid;
            """;
        cmd.Parameters.AddWithValue("$kb", knowledgeBaseId?.ToString("N") ?? (object)DBNull.Value);
        var rows = new List<(string ContentId, string DocumentId, string KnowledgeBaseId, string Text)>();
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        foreach (var row in rows)
        {
            SqliteDatabase.Execute(db, tx, """
                INSERT INTO content_embeddings(content_id,document_id,knowledge_base_id,model_id,chunk_version,dimension,vector,created_at)
                VALUES($content,$doc,$kb,$model,$chunk,$dimension,$vector,$created);
                """,
                ("$content", row.ContentId), ("$doc", row.DocumentId), ("$kb", row.KnowledgeBaseId),
                ("$model", embedder.ModelId), ("$chunk", ChunkVersion), ("$dimension", embedder.Dimension),
                ("$vector", WriteVector(embedder.EmbedPassage(row.Text))),
                ("$created", DateTimeOffset.UtcNow.ToString("O")));
        }
        tx.Commit();
    }

    public SemanticIndexAudit AuditIndex()
    {
        EnsureSchema();
        using var db = database.Open();
        var source = (int)SqliteDatabase.ScalarLong(db, """
            SELECT count(*) FROM parsed_content c
            JOIN documents d ON d.document_id=c.document_id AND d.knowledge_base_id=c.knowledge_base_id
            WHERE d.parse_status IN ('COMPLETED','PARTIAL');
            """);
        using var indexedCmd = db.CreateCommand();
        indexedCmd.CommandText = "SELECT count(*) FROM content_embeddings WHERE model_id=$model;";
        indexedCmd.Parameters.AddWithValue("$model", embedder.ModelId);
        var indexed = Convert.ToInt32(indexedCmd.ExecuteScalar());
        using var missingCmd = db.CreateCommand();
        missingCmd.CommandText = """
            SELECT count(*) FROM parsed_content c
            JOIN documents d ON d.document_id=c.document_id AND d.knowledge_base_id=c.knowledge_base_id
            LEFT JOIN content_embeddings e ON e.content_id=c.content_id AND e.knowledge_base_id=c.knowledge_base_id AND e.model_id=$model
            WHERE d.parse_status IN ('COMPLETED','PARTIAL') AND e.content_id IS NULL;
            """;
        missingCmd.Parameters.AddWithValue("$model", embedder.ModelId);
        var missing = Convert.ToInt32(missingCmd.ExecuteScalar());
        using var orphanCmd = db.CreateCommand();
        orphanCmd.CommandText = """
            SELECT count(*) FROM content_embeddings e
            LEFT JOIN parsed_content c ON c.content_id=e.content_id AND c.knowledge_base_id=e.knowledge_base_id
            WHERE e.model_id=$model AND c.content_id IS NULL;
            """;
        orphanCmd.Parameters.AddWithValue("$model", embedder.ModelId);
        var orphan = Convert.ToInt32(orphanCmd.ExecuteScalar());
        using var dimensionCmd = db.CreateCommand();
        dimensionCmd.CommandText = "SELECT count(*) FROM content_embeddings WHERE model_id=$model AND dimension!=$dimension;";
        dimensionCmd.Parameters.AddWithValue("$model", embedder.ModelId);
        dimensionCmd.Parameters.AddWithValue("$dimension", embedder.Dimension);
        var dimensionMismatch = Convert.ToInt32(dimensionCmd.ExecuteScalar());
        var clean = SqliteDatabase.ScalarLong(db, "SELECT count(*) FROM pragma_foreign_key_check;") == 0;
        return new SemanticIndexAudit(source, indexed, missing, orphan, dimensionMismatch, clean);
    }

    private void EnsureSchema()
    {
        using var db = database.Open();
        using var tx = db.BeginTransaction();
        SqliteDatabase.Execute(db, tx, """
            CREATE TABLE IF NOT EXISTS content_embeddings(
              content_id TEXT NOT NULL,
              document_id TEXT NOT NULL,
              knowledge_base_id TEXT NOT NULL,
              model_id TEXT NOT NULL,
              chunk_version INTEGER NOT NULL,
              dimension INTEGER NOT NULL,
              vector BLOB NOT NULL,
              created_at TEXT NOT NULL,
              PRIMARY KEY(content_id, model_id),
              FOREIGN KEY(knowledge_base_id, document_id) REFERENCES parsed_documents(knowledge_base_id, document_id) ON DELETE CASCADE);
            """);
        SqliteDatabase.Execute(db, tx, "CREATE INDEX IF NOT EXISTS content_embeddings_by_kb_model ON content_embeddings(knowledge_base_id, model_id);");
        tx.Commit();
    }

    private static byte[] WriteVector(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] ReadVector(byte[] bytes, int dimension)
    {
        if (bytes.Length != dimension * sizeof(float)) return [];
        var vector = new float[dimension];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    private static double Dot(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        if (left.Count != right.Count) return 0;
        var score = 0d;
        for (var i = 0; i < left.Count; i++) score += left[i] * right[i];
        return score;
    }

    private static int? NullableInt(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetInt32(index);
    private static string? NullableString(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetString(index);
}

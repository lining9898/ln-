using AiKnowledgeAssistant.Core.Documents;
using Microsoft.Data.Sqlite;

namespace AiKnowledgeAssistant.Infrastructure.Database;

public sealed class SqliteDocumentRepository(SqliteDatabase database) : IDocumentRepository, IParsedContentRepository
{
    private const string Columns = "document_id,knowledge_base_id,original_file_name,managed_file_path,file_type,file_size,created_at,updated_at,parse_status,index_status,content_hash,parse_error,total_pages";
    public IReadOnlyList<Document> List(Guid knowledgeBaseId)
    {
        using var db = database.Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM documents WHERE knowledge_base_id=$id ORDER BY created_at DESC, document_id;";
        cmd.Parameters.AddWithValue("$id", knowledgeBaseId.ToString("N"));
        using var r = cmd.ExecuteReader(); var items = new List<Document>();
        while (r.Read()) items.Add(Read(r)); return items;
    }
    public Document? Get(Guid documentId)
    {
        using var db = database.Open(); return Find(db, null, documentId);
    }
    public bool HasDocuments(Guid knowledgeBaseId)
    {
        using var db = database.Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM documents WHERE knowledge_base_id=$id;";
        cmd.Parameters.AddWithValue("$id", knowledgeBaseId.ToString("N"));
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
    public void Add(Document document)
    {
        using var db = database.Open(); using var tx = db.BeginTransaction();
        try { Insert(db, tx, document); tx.Commit(); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            var existing = List(document.KnowledgeBaseId).FirstOrDefault(x =>
                string.Equals(x.ContentHash, document.ContentHash, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) throw new DuplicateDocumentException(existing);
            throw new InvalidOperationException("文档关联无效或 ID 重复，导入已取消。", e);
        }
    }
    public ParsedDocument? GetParsed(Guid documentId)
    {
        using var db = database.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT knowledge_base_id,parser_type,page_count,created_at FROM parsed_documents WHERE document_id=$id;";
        cmd.Parameters.AddWithValue("$id", documentId.ToString("N"));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var kb = Guid.Parse(r.GetString(0)); var parser = r.GetString(1);
        var pages = r.IsDBNull(2) ? (int?)null : r.GetInt32(2);
        var created = DateTimeOffset.Parse(r.GetString(3)); r.Close();
        var units = new List<ParsedUnit>();
        using (var unitCmd = db.CreateCommand())
        {
            unitCmd.CommandText = "SELECT sequence,text,source_type,page_number,section_title,section_path,start_line,end_line,paragraph_number,parser_type,created_at FROM parsed_content WHERE document_id=$id ORDER BY sequence;";
            unitCmd.Parameters.AddWithValue("$id", documentId.ToString("N"));
            using var ur = unitCmd.ExecuteReader();
            while (ur.Read()) units.Add(new ParsedUnit(documentId,kb,ur.GetInt32(0),ur.GetString(1),
                Enum.Parse<SourceType>(ur.GetString(2), true), NullableInt(ur,3), NullableString(ur,4),
                NullableString(ur,5),NullableInt(ur,6),NullableInt(ur,7),ur.GetString(9),
                DateTimeOffset.Parse(ur.GetString(10)), NullableInt(ur,8)));
        }
        var failures = new List<ParseFailure>();
        using (var failureCmd = db.CreateCommand())
        {
            failureCmd.CommandText = "SELECT page_number,reason FROM parse_failures WHERE document_id=$id ORDER BY rowid;";
            failureCmd.Parameters.AddWithValue("$id", documentId.ToString("N"));
            using var fr = failureCmd.ExecuteReader();
            while (fr.Read()) failures.Add(new ParseFailure(NullableInt(fr,0),fr.GetString(1)));
        }
        return new ParsedDocument(documentId,kb,parser,pages,units,failures,created);
    }
    public void BeginParsing(Guid documentId)
    {
        using var db = database.Open(); using var tx = db.BeginTransaction();
        var doc = Find(db,tx,documentId) ?? throw new InvalidOperationException("文档不存在，请刷新列表。");
        if (doc.ParseStatus == ProcessingStatus.Parsing) throw new InvalidOperationException("此文档已在解析中。");
        SqliteDatabase.Execute(db,tx,"UPDATE documents SET parse_status='PARSING',parse_error=NULL,updated_at=$now WHERE document_id=$id;",
            ("$now",DateTimeOffset.UtcNow.ToString("O")),("$id",documentId.ToString("N")));
        tx.Commit();
    }
    public void CompleteParsing(Guid documentId, ParsedDocument result)
    {
        using var db = database.Open(); using var tx = db.BeginTransaction();
        var doc = Find(db,tx,documentId) ?? throw new InvalidOperationException("文档不存在，请刷新列表。");
        if (doc.ParseStatus != ProcessingStatus.Parsing || doc.Id != result.DocumentId ||
            doc.KnowledgeBaseId != result.KnowledgeBaseId || result.Units is null || result.Failures is null ||
            result.Units.Any((u) => u.DocumentId != doc.Id || u.KnowledgeBaseId != doc.KnowledgeBaseId))
            throw new InvalidOperationException("解析结果与当前文档或解析状态不一致。");
        SqliteDatabase.Execute(db,tx,"DELETE FROM parsed_documents WHERE document_id=$id;",("$id",documentId.ToString("N")));
        InsertParsed(db,tx,result);
        var status = result.Units.Count == 0 ? ProcessingStatus.Failed :
            result.Failures.Count > 0 ? ProcessingStatus.Partial : ProcessingStatus.Completed;
        var summary = result.Failures.Count > 0 ? string.Join("；",result.Failures.Take(3).Select(f =>
            f.PageNumber is int p ? $"第 {p} 页：{f.Reason}" : f.Reason)) :
            status == ProcessingStatus.Failed ? "未提取到可用文本。" : null;
        SqliteDatabase.Execute(db,tx,"UPDATE documents SET parse_status=$status,parse_error=$error,total_pages=$pages,updated_at=$now WHERE document_id=$id;",
            ("$status",status.ToString().ToUpperInvariant()),("$error",summary),("$pages",result.PageCount),
            ("$now",DateTimeOffset.UtcNow.ToString("O")),("$id",documentId.ToString("N")));
        tx.Commit();
    }
    public void RecoverInterruptedParsing()
    {
        using var db = database.Open(); using var tx = db.BeginTransaction();
        SqliteDatabase.Execute(db,tx,"""
            UPDATE documents SET parse_status=CASE
              WHEN EXISTS(SELECT 1 FROM parsed_documents p WHERE p.document_id=documents.document_id)
              THEN CASE WHEN EXISTS(SELECT 1 FROM parsed_content c WHERE c.document_id=documents.document_id)
                 THEN CASE WHEN EXISTS(SELECT 1 FROM parse_failures f WHERE f.document_id=documents.document_id) THEN 'PARTIAL' ELSE 'COMPLETED' END
                 ELSE 'FAILED' END
              ELSE 'FAILED' END,
              parse_error='上次解析被中断；原有解析内容已保留，请重新解析。',
              updated_at=$now WHERE parse_status='PARSING';
            """,("$now",DateTimeOffset.UtcNow.ToString("O")));
        tx.Commit();
    }
    internal static void Insert(SqliteConnection db, SqliteTransaction tx, Document d) =>
        SqliteDatabase.Execute(db,tx,"INSERT INTO documents VALUES($id,$kb,$name,$path,$type,$size,$created,$updated,$parse,$index,$hash,$error,$pages);",
            ("$id",d.Id.ToString("N")),("$kb",d.KnowledgeBaseId.ToString("N")),("$name",d.OriginalFileName),
            ("$path",d.ManagedFilePath),("$type",d.FileType),("$size",d.FileSize),
            ("$created",d.CreatedAt.ToString("O")),("$updated",d.UpdatedAt.ToString("O")),
            ("$parse",d.ParseStatus.ToString().ToUpperInvariant()),("$index",d.IndexStatus.ToString().ToUpperInvariant()),
            ("$hash",d.ContentHash),("$error",d.ParseError),("$pages",d.TotalPages));
    internal static void InsertParsed(SqliteConnection db, SqliteTransaction tx, ParsedDocument p)
    {
        SqliteDatabase.Execute(db,tx,"INSERT INTO parsed_documents VALUES($doc,$kb,$parser,$pages,$created);",
            ("$doc",p.DocumentId.ToString("N")),("$kb",p.KnowledgeBaseId.ToString("N")),("$parser",p.ParserType),
            ("$pages",p.PageCount),("$created",p.CreatedAt.ToString("O")));
        foreach (var u in p.Units)
            SqliteDatabase.Execute(db,tx,"INSERT INTO parsed_content VALUES($id,$doc,$kb,$seq,$text,$source,$page,$title,$section,$start,$end,$para,$parser,$created);",
                ("$id",Guid.NewGuid().ToString("N")),("$doc",u.DocumentId.ToString("N")),("$kb",u.KnowledgeBaseId.ToString("N")),
                ("$seq",u.Sequence),("$text",u.Text),("$source",u.SourceType.ToString().ToUpperInvariant()),
                ("$page",u.PageNumber),("$title",u.SectionTitle),("$section",u.SectionPath),
                ("$start",u.StartLine),("$end",u.EndLine),("$para",u.ParagraphNumber),
                ("$parser",u.ParserType),("$created",u.CreatedAt.ToString("O")));
        foreach (var f in p.Failures)
            SqliteDatabase.Execute(db,tx,"INSERT INTO parse_failures VALUES($id,$doc,$kb,$page,$reason);",
                ("$id",Guid.NewGuid().ToString("N")),("$doc",p.DocumentId.ToString("N")),
                ("$kb",p.KnowledgeBaseId.ToString("N")),("$page",f.PageNumber),("$reason",f.Reason));
    }
    private static Document? Find(SqliteConnection db, SqliteTransaction? tx, Guid id)
    {
        using var cmd = db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText=$"SELECT {Columns} FROM documents WHERE document_id=$id;";
        cmd.Parameters.AddWithValue("$id",id.ToString("N"));
        using var r=cmd.ExecuteReader();return r.Read()?Read(r):null;
    }
    private static Document Read(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),
        r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt64(5),DateTimeOffset.Parse(r.GetString(6)),
        DateTimeOffset.Parse(r.GetString(7)),Enum.Parse<ProcessingStatus>(r.GetString(8),true),
        Enum.Parse<ProcessingStatus>(r.GetString(9),true),r.GetString(10),NullableString(r,11),NullableInt(r,12));
    private static int? NullableInt(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetInt32(i);
    private static string? NullableString(SqliteDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
}

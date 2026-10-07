using System.Security.Cryptography;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Infrastructure.Database;
using AiKnowledgeAssistant.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

var failures = new List<string>();
void Check(bool condition, string name) { if (!condition) failures.Add(name); Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}"); }
string root = Path.Combine(Path.GetTempPath(), "aka-batch5-中文 空格-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var paths = new UserDataPaths(root); paths.EnsureDirectories();
    var legacyKb = new JsonKnowledgeBaseStore(paths);
    var legacyDocs = new JsonDocumentRepository(paths);
    var a = legacyKb.Create("中文 规范库"); var b = legacyKb.Create("公司 资料库");
    Document MakeDoc(Guid kb, string name, string text)
    {
        var id = Guid.NewGuid(); var path = new ManagedDocumentPaths(paths).FileFor(kb,id,"TXT");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path,text);
        var now = DateTimeOffset.UtcNow;
        return new Document(id,kb,name,path,"TXT",new FileInfo(path).Length,now,now,
            ProcessingStatus.Pending,ProcessingStatus.Pending,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }
    var da = MakeDoc(a.Id,"中文 文件.txt","第一行\n第二行");
    var dbDoc = MakeDoc(b.Id,"中文 文件.txt","另一个知识库");
    legacyDocs.Add(da); legacyDocs.Add(dbDoc);
    legacyDocs.BeginParsing(da.Id);
    var unit = new ParsedUnit(da.Id,a.Id,1,"第一行",SourceType.Text,null,"标题","标题",1,1,"TXT",DateTimeOffset.UtcNow);
    legacyDocs.CompleteParsing(da.Id,new ParsedDocument(da.Id,a.Id,"TXT",null,[unit],[],DateTimeOffset.UtcNow));
    var originalBytes = File.ReadAllBytes(da.ManagedFilePath);
    var kbJson = Path.Combine(paths.Databases,"knowledge-bases.json");
    var docJson = Path.Combine(paths.Databases,"documents.json");
    var oldKbJson = File.ReadAllBytes(kbJson); var oldDocJson = File.ReadAllBytes(docJson);
    var database = new SqliteDatabase(paths); database.Initialize();
    new LegacyJsonMigration(database).MigrateIfNeeded();
    var audit = database.Audit();
    Console.WriteLine($"AUDIT knowledge_bases={audit.KnowledgeBaseCount} documents={audit.DocumentCount} parsed_content={audit.ParsedContentCount} orphan_documents={audit.OrphanDocumentCount} orphan_content={audit.OrphanContentCount} foreign_key_check={(audit.ForeignKeyClean ? "CLEAN" : "FAIL")}");
    Check(audit.KnowledgeBaseCount==2 && audit.DocumentCount==2 && audit.ParsedContentCount==1,"旧 JSON 知识库/文档/解析内容迁移");
    Check(audit.OrphanDocumentCount==0 && audit.OrphanContentCount==0 && audit.ForeignKeyClean,"外键与孤立记录审计");
    var repo = new SqliteDocumentRepository(database);
    var migrated = repo.Get(da.Id)!; var parsed = repo.GetParsed(da.Id)!;
    Check(migrated.ContentHash==da.ContentHash && migrated.ParseStatus==ProcessingStatus.Completed && migrated.IndexStatus==ProcessingStatus.Pending && migrated.OriginalFileName==da.OriginalFileName,"SHA-256、状态、中文文件名保留");
    Check(parsed.Units.Count==1 && parsed.Units[0].StartLine==1 && parsed.Units[0].SourceType==SourceType.Text,"来源元数据保留");
    Check(repo.List(a.Id).Count==1 && repo.List(b.Id).Count==1,"知识库隔离与同名文件");
    new LegacyJsonMigration(database).MigrateIfNeeded();
    Check(database.Audit()==audit,"重复启动迁移幂等");
    var sqlKb = new SqliteKnowledgeBaseStore(database); sqlKb.Create("新知识库");
    Check(File.ReadAllBytes(kbJson).SequenceEqual(oldKbJson) && File.ReadAllBytes(docJson).SequenceEqual(oldDocJson),"SQLite 写入不更新 JSON");
    Check(File.ReadAllBytes(da.ManagedFilePath).SequenceEqual(originalBytes),"受管理原文件未修改");
    using (var conn = database.Open())
    {
        using var cmd = conn.CreateCommand(); cmd.CommandText="PRAGMA foreign_keys;";
        Check(Convert.ToInt64(cmd.ExecuteScalar())==1,"每个连接开启 foreign_keys");
        using var tx = conn.BeginTransaction();
        try { SqliteTestInsertInvalid(conn,tx,a.Id); tx.Commit(); failures.Add("跨知识库外键拒绝"); }
        catch (SqliteException) { Check(true,"跨知识库外键拒绝"); }
    }
    repo.BeginParsing(da.Id);
    using (var conn = database.Open())
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText="CREATE TRIGGER fail_reparse BEFORE INSERT ON parsed_content BEGIN SELECT RAISE(ABORT,'test rollback'); END;";
        cmd.ExecuteNonQuery();
    }
    try { repo.CompleteParsing(da.Id,new ParsedDocument(da.Id,a.Id,"TXT",null,[unit with { Text="替换" }],[],DateTimeOffset.UtcNow)); failures.Add("解析替换事务回滚"); }
    catch (SqliteException) { Check(repo.GetParsed(da.Id)!.Units[0].Text=="第一行","解析替换事务回滚"); }
    using (var conn = database.Open()) { using var cmd = conn.CreateCommand(); cmd.CommandText="DROP TRIGGER fail_reparse;"; cmd.ExecuteNonQuery(); }
    repo.RecoverInterruptedParsing();
    Check(repo.Get(da.Id)!.ParseStatus==ProcessingStatus.Completed && repo.GetParsed(da.Id) is not null,"中断恢复保留旧有效解析结果");
    // Exercise every source location and status in the SQLite repository.
    var pageDoc = MakeDoc(a.Id,"扫描 页.pdf","pdf-bytes");
    pageDoc = pageDoc with { FileType="PDF", ManagedFilePath=new ManagedDocumentPaths(paths).FileFor(a.Id,pageDoc.Id,"PDF") };
    File.Move(new ManagedDocumentPaths(paths).FileFor(a.Id,pageDoc.Id,"TXT"),pageDoc.ManagedFilePath);
    repo.Add(pageDoc); repo.BeginParsing(pageDoc.Id);
    var pdfUnits = new[] {
        new ParsedUnit(pageDoc.Id,a.Id,1,"文本页 中文",SourceType.Text,55,null,null,null,null,"PDF",DateTimeOffset.UtcNow),
        new ParsedUnit(pageDoc.Id,a.Id,2,"扫描页 OCR",SourceType.Ocr,56,null,null,null,null,"PDF",DateTimeOffset.UtcNow) };
    repo.CompleteParsing(pageDoc.Id,new ParsedDocument(pageDoc.Id,a.Id,"PDF",100,pdfUnits,[new ParseFailure(57,"OCR 失败")],DateTimeOffset.UtcNow));
    var pdfSaved=repo.GetParsed(pageDoc.Id)!;
    Check(repo.Get(pageDoc.Id)!.ParseStatus==ProcessingStatus.Partial && repo.Get(pageDoc.Id)!.ParseError!.Contains("57") &&
        pdfSaved.PageCount==100 && pdfSaved.Units[0].PageNumber==55 && pdfSaved.Units[1].SourceType==SourceType.Ocr,
        "PDF 物理页码、TEXT/OCR、PARTIAL、失败页迁移字段");
    var docxDoc=MakeDoc(a.Id,"标题.docx","docx-bytes");
    docxDoc=docxDoc with { FileType="DOCX", ManagedFilePath=new ManagedDocumentPaths(paths).FileFor(a.Id,docxDoc.Id,"DOCX") };
    File.Move(new ManagedDocumentPaths(paths).FileFor(a.Id,docxDoc.Id,"TXT"),docxDoc.ManagedFilePath);
    repo.Add(docxDoc);repo.BeginParsing(docxDoc.Id);
    var heading=new ParsedUnit(docxDoc.Id,a.Id,1,"正文",SourceType.Text,null,"第二章","总则 / 第二章",null,null,"DOCX",DateTimeOffset.UtcNow,3);
    repo.CompleteParsing(docxDoc.Id,new ParsedDocument(docxDoc.Id,a.Id,"DOCX",null,[heading],[],DateTimeOffset.UtcNow));
    Check(repo.GetParsed(docxDoc.Id)!.Units[0].SectionPath=="总则 / 第二章" && repo.GetParsed(docxDoc.Id)!.Units[0].ParagraphNumber==3,"DOCX 章节与段落位置");
    var mdDoc=MakeDoc(b.Id,"标题.md","# 标题");
    mdDoc=mdDoc with { FileType="MD", ManagedFilePath=new ManagedDocumentPaths(paths).FileFor(b.Id,mdDoc.Id,"MD") };
    File.Move(new ManagedDocumentPaths(paths).FileFor(b.Id,mdDoc.Id,"TXT"),mdDoc.ManagedFilePath);
    repo.Add(mdDoc);repo.BeginParsing(mdDoc.Id);
    var mdUnit=new ParsedUnit(mdDoc.Id,b.Id,1,"内容",SourceType.Text,null,"标题","标题",2,4,"MARKDOWN",DateTimeOffset.UtcNow);
    repo.CompleteParsing(mdDoc.Id,new ParsedDocument(mdDoc.Id,b.Id,"MARKDOWN",null,[mdUnit],[],DateTimeOffset.UtcNow));
    Check(repo.GetParsed(mdDoc.Id)!.Units[0].EndLine==4 && repo.GetParsed(mdDoc.Id)!.Units[0].SectionTitle=="标题","Markdown 章节与行号");
    Check(database.Audit().OrphanContentCount==0 && database.Audit().ForeignKeyClean,"新增解析内容外键审计");

    var capabilities = database.ProbeCapabilities();
    Console.WriteLine($"SQLite_VERSION={capabilities.Version} FTS5_AVAILABLE={(capabilities.Fts5Available?"YES":"NO")}");
    Check(capabilities.Fts5Available,"FTS5 实际 smoke test");

    // A malformed legacy snapshot must leave JSON intact and SQLite empty, then be retriable.
    var badRoot=Path.Combine(root,"bad"); var badPaths=new UserDataPaths(badRoot);badPaths.EnsureDirectories();
    File.WriteAllText(Path.Combine(badPaths.Databases,"knowledge-bases.json"),"{invalid");
    var badDatabase=new SqliteDatabase(badPaths);badDatabase.Initialize();
    try { new LegacyJsonMigration(badDatabase).MigrateIfNeeded(); failures.Add("迁移失败回滚"); }
    catch (InvalidDataException) { Check(badDatabase.Audit().KnowledgeBaseCount==0 && File.Exists(Path.Combine(badPaths.Databases,"knowledge-bases.json")),"迁移失败回滚与旧文件保留"); }
    // BATCH 3 document snapshot used schema_version 1 without parsed_documents.
    var v1Root=Path.Combine(root,"v1");var v1Paths=new UserDataPaths(v1Root);v1Paths.EnsureDirectories();
    var v1Kb=new JsonKnowledgeBaseStore(v1Paths).Create("旧版本资料库");
    var v1DocId=Guid.NewGuid();var v1Path=new ManagedDocumentPaths(v1Paths).FileFor(v1Kb.Id,v1DocId,"TXT");
    Directory.CreateDirectory(Path.GetDirectoryName(v1Path)!);File.WriteAllText(v1Path,"旧文件");
    var v1Time=DateTimeOffset.UtcNow;
    var v1Doc=new Document(v1DocId,v1Kb.Id,"旧 文件.txt",v1Path,"TXT",new FileInfo(v1Path).Length,v1Time,v1Time,
        ProcessingStatus.Pending,ProcessingStatus.Pending,Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(v1Path))));
    new JsonDocumentRepository(v1Paths).Add(v1Doc);
    var v1File=Path.Combine(v1Paths.Databases,"documents.json");
    var v1Json=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(v1File))!.AsObject();
    v1Json["schema_version"]=1;v1Json.Remove("parsed_documents");File.WriteAllText(v1File,v1Json.ToJsonString());
    var v1Database=new SqliteDatabase(v1Paths);v1Database.Initialize();
    using (var conn=v1Database.Open()) { using var cmd=conn.CreateCommand();
        cmd.CommandText="CREATE TRIGGER fail_migration BEFORE INSERT ON documents BEGIN SELECT RAISE(ABORT,'test migration rollback'); END;";cmd.ExecuteNonQuery(); }
    try { new LegacyJsonMigration(v1Database).MigrateIfNeeded(); failures.Add("事务中途迁移失败回滚"); }
    catch (SqliteException) {
        Check(v1Database.Audit().KnowledgeBaseCount==0 && v1Database.Audit().DocumentCount==0 &&
            File.Exists(v1File),"事务中途迁移失败回滚");
    }
    using (var conn=v1Database.Open()) { using var cmd=conn.CreateCommand();cmd.CommandText="DROP TRIGGER fail_migration;";cmd.ExecuteNonQuery(); }
    new LegacyJsonMigration(v1Database).MigrateIfNeeded();
    Check(v1Database.Audit().DocumentCount==1 && new SqliteDocumentRepository(v1Database).Get(v1DocId)!.OriginalFileName=="旧 文件.txt",
        "BATCH 3 JSON schema v1 迁移");
    using (var conn=v1Database.Open())
    {
        using var cmd=conn.CreateCommand();cmd.CommandText="SELECT version FROM schema_version;";
        Check(Convert.ToInt64(cmd.ExecuteScalar())==1,"schema_version=1");
        cmd.CommandText="UPDATE schema_version SET version=999;";cmd.ExecuteNonQuery();
    }
    try {v1Database.Initialize();failures.Add("不支持的 Schema 版本保留");}
    catch (InvalidDataException) {Check(File.Exists(v1Database.FilePath),"不支持的 Schema 版本保留");}
    var damagedRoot=Path.Combine(root,"damaged");var damagedPaths=new UserDataPaths(damagedRoot);damagedPaths.EnsureDirectories();
    var damagedFile=Path.Combine(damagedPaths.Databases,"knowledge.db");File.WriteAllText(damagedFile,"broken database");
    try { new SqliteDatabase(damagedPaths).Initialize();failures.Add("损坏数据库保留"); }
    catch (InvalidDataException) { Check(File.ReadAllText(damagedFile)=="broken database","损坏数据库保留"); }
}
finally { try { Directory.Delete(root,true); } catch {} }
if (failures.Count>0) { Console.Error.WriteLine("FAILURES: "+string.Join(", ",failures)); Environment.Exit(1); }
static void SqliteTestInsertInvalid(SqliteConnection conn,SqliteTransaction tx,Guid kb)
{
    using var cmd=conn.CreateCommand();cmd.Transaction=tx;
    cmd.CommandText="INSERT INTO parsed_content(content_id,document_id,knowledge_base_id,sequence,text,source_type,parser_type,created_at) VALUES($id,$doc,$kb,1,'x','TEXT','TXT','2026-01-01');";
    cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("N"));
    cmd.Parameters.AddWithValue("$doc",Guid.NewGuid().ToString("N"));cmd.Parameters.AddWithValue("$kb",kb.ToString("N"));cmd.ExecuteNonQuery();
}

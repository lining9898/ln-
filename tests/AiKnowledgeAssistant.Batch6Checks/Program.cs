using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Desktop.ViewModels;
using AiKnowledgeAssistant.Infrastructure.Database;
using AiKnowledgeAssistant.Infrastructure.Documents;
using AiKnowledgeAssistant.Infrastructure.OCR;
using AiKnowledgeAssistant.Infrastructure.Parser;
using AiKnowledgeAssistant.Infrastructure.Retrieval;
using AiKnowledgeAssistant.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

var root = Path.Combine(Path.GetTempPath(),"aka-search-中文 空格-"+Guid.NewGuid().ToString("N"));
var failures = new List<string>();
void Check(bool condition,string name)
{
    Console.WriteLine($"{(condition?"PASS":"FAIL")} {name}");
    if(!condition) failures.Add(name);
}
try
{
    var paths=new UserDataPaths(root);paths.EnsureDirectories();
    var database=new SqliteDatabase(paths);database.Initialize();
    new LegacyJsonMigration(database).MigrateIfNeeded();
    var bases=new SqliteKnowledgeBaseStore(database);
    var docs=new SqliteDocumentRepository(database);
    var search=new SqliteDocumentSearch(database);
    var kbA=bases.Create("中文资料库");var kbB=bases.Create("English Manual");
    var importer=new DocumentImportService(bases,docs,paths);
    var parsing=new DocumentParsingService(docs,docs,[
        new PdfDocumentParser(new TesseractPdfPageOcr("/missing-pdftoppm","/missing-tesseract")),
        new DocxDocumentParser(),new TextDocumentParser(),new MarkdownDocumentParser()]);
    string fixture(string name)=>Path.GetFullPath(Path.Combine("tests","fixtures","batch4",name));
    var pdf=importer.Import(kbA.Id,fixture("text_pages.pdf"));Check(parsing.Reparse(pdf.Id).ParseStatus==ProcessingStatus.Completed,"真实 PDF 格式解析并建立索引");
    var docx=importer.Import(kbA.Id,fixture("headings_table.docx"));Check(parsing.Reparse(docx.Id).ParseStatus==ProcessingStatus.Completed,"真实 DOCX 格式解析并建立索引");
    var md=importer.Import(kbA.Id,fixture("headings 中文.md"));Check(parsing.Reparse(md.Id).ParseStatus==ProcessingStatus.Completed,"Markdown 解析并建立索引");
    var txt=importer.Import(kbA.Id,fixture("lines 中文.txt"));Check(parsing.Reparse(txt.Id).ParseStatus==ProcessingStatus.Completed,"TXT 解析并建立索引");
    var numFile=Path.Combine(root,"编号 English 2026.txt");
    File.WriteAllText(numFile,"项目编号 GB 50010 6.2.10。Install device ZX-1042 on level 3.\n第二行：公司报销标准 2026。\n");
    var numberDoc=importer.Import(kbB.Id,numFile);parsing.Reparse(numberDoc.Id);
    var chinese=search.Search("中文第一章",[kbA.Id]);
    Check(chinese.Any(h=>h.DocumentId==pdf.Id && h.PageNumber==1 && h.Text.Contains("中文第一章")),"中文关键词命中 PDF 物理第一页且原文一致");
    using (var conn=database.Open())
    {
        var hit=chinese.First(h=>h.DocumentId==pdf.Id);
        using var cmd=conn.CreateCommand();
        cmd.CommandText="SELECT count(*) FROM parsed_content WHERE content_id=$id AND document_id=$doc AND knowledge_base_id=$kb AND text=$text AND page_number=$page;";
        cmd.Parameters.AddWithValue("$id",hit.ContentId.ToString("N"));
        cmd.Parameters.AddWithValue("$doc",hit.DocumentId.ToString("N"));
        cmd.Parameters.AddWithValue("$kb",hit.KnowledgeBaseId.ToString("N"));
        cmd.Parameters.AddWithValue("$text",hit.Text);cmd.Parameters.AddWithValue("$page",hit.PageNumber!.Value);
        Check(Convert.ToInt64(cmd.ExecuteScalar())==1 && hit.FileName==pdf.OriginalFileName && hit.SourceType==SourceType.Text,
            "结果 ID、文件名、正文、页码与 SQLite 原始来源逐项一致");
    }
    Check(search.Search("English first page",[kbA.Id]).Any(h=>h.DocumentId==pdf.Id),"英文关键词命中");
    Check(search.Search("6.2.10",[kbB.Id]).Any(h=>h.DocumentId==numberDoc.Id),"数字编号精确命中");
    Check(search.Search("\"GB 50010 6.2.10\"",[kbB.Id]).Any(h=>h.DocumentId==numberDoc.Id),"编号精确短语命中");
    Check(search.Search("\"GB 50010 6.2.11\"",[kbB.Id]).Count==0,"错误精确短语不命中");
    Check(search.Search("GB 50010 6.2.10",[kbA.Id]).Count==0,"知识库过滤隔离");
    var docxHit=search.Search("第二节正文",[kbA.Id]).FirstOrDefault(h=>h.DocumentId==docx.Id);
    Check(docxHit is not null && docxHit.SectionTitle=="第二节 技术说明" && docxHit.ParagraphNumber is >0,"DOCX 标题与段落来源");
    var mdHit=search.Search("段落 English",[kbA.Id]).FirstOrDefault(h=>h.DocumentId==md.Id);
    Check(mdHit is not null && mdHit.StartLine is >0 && mdHit.EndLine>=mdHit.StartLine,"Markdown 行号来源");
    Check(search.Search("中文第一行",[kbA.Id]).Any(h=>h.DocumentId==txt.Id && h.StartLine==1),"TXT 中文与行号来源");
    var rankExactPath=Path.Combine(root,"相关性-精确.txt");
    var rankLoosePath=Path.Combine(root,"相关性-分散.txt");
    File.WriteAllText(rankExactPath,"设备安装流程。设备安装注意事项。");
    File.WriteAllText(rankLoosePath,"设备与其他设施分别检查，安装时记录。");
    var rankExact=importer.Import(kbB.Id,rankExactPath);parsing.Reparse(rankExact.Id);
    var rankLoose=importer.Import(kbB.Id,rankLoosePath);parsing.Reparse(rankLoose.Id);
    var ranked=search.Search("安装",[kbB.Id]);
    Check(ranked.Count>=2 && ranked[0].DocumentId==rankExact.Id && ranked[0].Score>ranked[1].Score,
        "相关性排序优先高相关原文");
    Guid? selectedKb=kbA.Id;
    var searchVm=new DocumentSearchViewModel(search,()=>selectedKb) { Query="中文第一章" };
    searchVm.SearchCommand.Execute(null);
    Check(searchVm.Results.Any(x=>x.Hit.DocumentId==pdf.Id && x.Position.Contains("第 1 页")),"中文搜索 UI ViewModel 结果与来源");
    selectedKb=kbB.Id;searchVm.ClearResults();
    Check(searchVm.Results.Count==0,"切换知识库清除旧结果");
    var before=search.AuditIndex();
    Check(before.IsConsistent && before.SourceCount==before.IndexedCount,"首次索引一致性");
    var firstContent=chinese.First(h=>h.DocumentId==pdf.Id).ContentId;
    parsing.Reparse(pdf.Id);
    var afterReparse=search.AuditIndex();
    Check(afterReparse.IsConsistent && afterReparse.SourceCount==afterReparse.IndexedCount &&
        search.Search("中文第一章",[kbA.Id]).Count(h=>h.DocumentId==pdf.Id)==1,"重复解析索引不重复");
    Check(search.Search("中文第一章",[kbA.Id]).First(h=>h.DocumentId==pdf.Id).ContentId!=firstContent,"重新解析替换内容 ID");
    // Simulate a persisted BATCH 5 schema-v1 database and verify v2 backfill.
    using (var conn=database.Open())
    {
        using var cmd=conn.CreateCommand();
        cmd.CommandText="DROP TRIGGER parsed_content_fts_delete; DROP TRIGGER parsed_content_fts_text_update; DROP TABLE content_fts; UPDATE schema_version SET version=1; UPDATE documents SET index_status='PENDING';";
        cmd.ExecuteNonQuery();
    }
    var restarted=new SqliteDatabase(paths);restarted.Initialize();new LegacyJsonMigration(restarted).MigrateIfNeeded();
    Check(restarted.Audit().ForeignKeyClean && new SqliteDocumentSearch(restarted).AuditIndex().IsConsistent,
        "BATCH 5 schema v1 升级 v2 并回填全文索引");
    var searchAfterRestart=new SqliteDocumentSearch(restarted);
    Check(searchAfterRestart.Search("中文第一章",[kbA.Id]).Any(h=>h.DocumentId==pdf.Id),"软件重启后仍可搜索");
    // OCR content is explicitly marked, never promoted to verified TEXT.
    docs.BeginParsing(pdf.Id);
    var old=docs.GetParsed(pdf.Id)!;
    docs.CompleteParsing(pdf.Id,old with { Units=[old.Units[0] with {SourceType=SourceType.Ocr}] });
    Check(search.Search("中文第一章",[kbA.Id]).Any(h=>h.SourceType==SourceType.Ocr),"OCR 来源保持机器识别标识");
    using (var conn=database.Open())
    {
        using var cmd=conn.CreateCommand();cmd.CommandText="DELETE FROM documents WHERE document_id=$id;";cmd.Parameters.AddWithValue("$id",numberDoc.Id.ToString("N"));cmd.ExecuteNonQuery();
    }
    Check(search.Search("6.2.10",[kbB.Id]).Count==0 && search.AuditIndex().IsConsistent,"删除文档后无失效命中");
    using (var conn=database.Open())
    {
        using var cmd=conn.CreateCommand();cmd.CommandText="DELETE FROM content_fts WHERE rowid=(SELECT rowid FROM parsed_content LIMIT 1);";cmd.ExecuteNonQuery();
    }
    Check(search.AuditIndex().MissingCount==1,"审计发现缺失索引");
    search.RebuildIndex();
    Check(search.AuditIndex().IsConsistent,"重新索引恢复一致性");
    using (var conn=database.Open())
    {
        using var cmd=conn.CreateCommand();
        cmd.CommandText="UPDATE parsed_content SET text='更新后的中文检索文本' WHERE document_id=$id AND sequence=1;";
        cmd.Parameters.AddWithValue("$id",md.Id.ToString("N"));cmd.ExecuteNonQuery();
    }
    Check(search.AuditIndex().MissingCount==1 && docs.Get(md.Id)!.IndexStatus==ProcessingStatus.Pending,
        "绕过 Repository 修改原文时索引失效并标记待重建");
    search.RebuildIndex(kbA.Id);
    Check(search.Search("更新后的中文检索文本",[kbA.Id]).Any(h=>h.DocumentId==md.Id) &&
        search.AuditIndex().IsConsistent,"按知识库重建索引并恢复搜索");
    var sourceText=docs.GetParsed(pdf.Id)!.Units[0].Text;
    using (var conn=database.Open())
    {
        using var cmd=conn.CreateCommand();cmd.CommandText="DROP TABLE content_fts;";cmd.ExecuteNonQuery();
    }
    try { search.RebuildIndex(); failures.Add("索引重建失败保留原文"); }
    catch (SqliteException) { Check(docs.GetParsed(pdf.Id)!.Units[0].Text==sourceText,"索引重建失败保留原文"); }
    Console.WriteLine($"AUDIT_BEFORE source={before.SourceCount} indexed={before.IndexedCount} missing={before.MissingCount} orphan={before.OrphanCount} stale={before.StaleCount}");
}
finally { try { Directory.Delete(root,true); } catch {} }
if(failures.Count>0){Console.Error.WriteLine("FAILURES="+string.Join(",",failures));Environment.Exit(1);}

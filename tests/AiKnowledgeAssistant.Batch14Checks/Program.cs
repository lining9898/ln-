using System.IO.Compression;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Infrastructure.Backup;
using AiKnowledgeAssistant.Infrastructure.Database;
using AiKnowledgeAssistant.Infrastructure.Documents;
using AiKnowledgeAssistant.Infrastructure.OCR;
using AiKnowledgeAssistant.Infrastructure.Parser;
using AiKnowledgeAssistant.Infrastructure.Retrieval;
using AiKnowledgeAssistant.Infrastructure.Storage;

var root = Path.Combine(Path.GetTempPath(), "aka-backup-中文 空格-" + Guid.NewGuid().ToString("N"));
var failures = new List<string>();
void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
    if (!condition) failures.Add(name);
}
void Reject<T>(Action action, string name) where T : Exception
{
    var rejected = false;
    try { action(); } catch (T) { rejected = true; }
    Check(rejected, name);
}

try
{
    var sourcePaths = new UserDataPaths(Path.Combine(root, "源 用户"));
    sourcePaths.EnsureDirectories();
    var sourceDb = new SqliteDatabase(sourcePaths); sourceDb.Initialize();
    new LegacyJsonMigration(sourceDb).MigrateIfNeeded();
    var bases = new SqliteKnowledgeBaseStore(sourceDb);
    var docs = new SqliteDocumentRepository(sourceDb);
    var importer = new DocumentImportService(bases, docs, sourcePaths);
    var parsing = new DocumentParsingService(docs, docs, [
        new PdfDocumentParser(new TesseractPdfPageOcr("/missing-pdftoppm", "/missing-tesseract")),
        new DocxDocumentParser(), new TextDocumentParser(), new MarkdownDocumentParser()]);
    var kb = bases.Create("备份资料库");
    var sourceFile = Path.Combine(root, "中文 原文件.txt");
    File.WriteAllText(sourceFile, "备份恢复需要保留原文件、SQLite 和索引。");
    var document = importer.Import(kb.Id, sourceFile);
    Check(parsing.Reparse(document.Id).ParseStatus == ProcessingStatus.Completed, "备份前资料解析完成");
    var backupPath = Path.Combine(root, "backup.zip");
    var manifest = new LocalBackupService(sourceDb, sourcePaths).CreateBackup(backupPath);
    Check(File.Exists(backupPath) && manifest.Files.Any(f => f.Path == "databases/knowledge.db") &&
        manifest.Files.Any(f => f.Path.StartsWith("documents/", StringComparison.Ordinal)),
        "备份 ZIP 包含 manifest、数据库和受管理原文件");
    var restorePaths = new UserDataPaths(Path.Combine(root, "恢复 用户"));
    restorePaths.EnsureDirectories();
    var restoreDb = new SqliteDatabase(restorePaths);
    var restoredManifest = new LocalBackupService(restoreDb, restorePaths).RestoreBackup(backupPath);
    restoreDb.Initialize();
    var restoredDocs = new SqliteDocumentRepository(restoreDb);
    var restoredSearch = new SqliteDocumentSearch(restoreDb);
    Check(restoredManifest.Files.Count == manifest.Files.Count &&
        restoredDocs.Get(document.Id) is { } restored &&
        File.Exists(restored.ManagedFilePath) &&
        restoredSearch.Search("备份恢复", [kb.Id]).Any(h => h.DocumentId == document.Id),
        "恢复后文档、原文件和全文检索可用");
    var occupiedPaths = new UserDataPaths(Path.Combine(root, "已有数据"));
    occupiedPaths.EnsureDirectories();
    File.WriteAllText(Path.Combine(occupiedPaths.Databases, "keep.txt"), "do not overwrite");
    Reject<InvalidOperationException>(() => new LocalBackupService(new SqliteDatabase(occupiedPaths), occupiedPaths).RestoreBackup(backupPath),
        "恢复默认不覆盖已有数据");
    var malicious = Path.Combine(root, "malicious.zip");
    using (var zip = ZipFile.Open(malicious, ZipArchiveMode.Create))
        zip.CreateEntry("../evil.txt");
    var emptyPaths = new UserDataPaths(Path.Combine(root, "恶意恢复"));
    emptyPaths.EnsureDirectories();
    Reject<InvalidDataException>(() => new LocalBackupService(new SqliteDatabase(emptyPaths), emptyPaths).RestoreBackup(malicious),
        "恢复拒绝路径穿越条目");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAILURES=" + string.Join(",", failures));
    Environment.Exit(1);
}

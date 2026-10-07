using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Infrastructure.Documents;
using AiKnowledgeAssistant.Infrastructure.Storage;
using AiKnowledgeAssistant.Desktop.ViewModels;

if (args.Length > 0 && args[0] == "--probe")
{
    var paths = new UserDataPaths(args[1]);
    var documents = new JsonDocumentRepository(paths).List(Guid.Parse(args[2]));
    if (documents.Count != int.Parse(args[3]) || documents.Any(d =>
        !File.Exists(d.ManagedFilePath) ||
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(d.ManagedFilePath))).ToLowerInvariant() != d.ContentHash))
        return 2;
    Console.WriteLine("独立新进程已验证文档记录、副本和 SHA-256");
    return 0;
}
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
void Reject<T>(Action action, string name) where T : Exception
{
    bool rejected = false;
    try { action(); } catch (T) { rejected = true; }
    Check(rejected, name);
}
var testRoot = Path.Combine(Path.GetTempPath(), "aka-batch3-" + Guid.NewGuid().ToString("N"));
try
{
    var basePath = Path.Combine(testRoot, "中文用户 空格");
    var paths = new UserDataPaths(basePath);
    paths.EnsureDirectories();
    var originalRoot = Path.Combine(testRoot, "原始资料 空格");
    Directory.CreateDirectory(originalRoot);
    string Source(string name, byte[] content)
    {
        var path = Path.Combine(originalRoot, name);
        File.WriteAllBytes(path, content);
        return path;
    }
    var knowledgeBases = new JsonKnowledgeBaseStore(paths);
    var a = knowledgeBases.Create("项目资料 A");
    var b = knowledgeBases.Create("公司制度 B");
    var repository = new JsonDocumentRepository(paths);
    var importer = new DocumentImportService(knowledgeBases, repository, paths);
    var examples = new[]
    {
        Source("中文 PDF.pdf", [0x25, 0x50, 0x44, 0x46, 0x01]),
        Source("会议 纪要.docx", [0x50, 0x4b, 0x03, 0x04, 0x02]),
        Source("说明 文档.txt", "中文内容 一号"u8.ToArray()),
        Source("说明 文档.md", "# 内容 二号"u8.ToArray())
    };
    var imported = examples.Select(file => importer.Import(a.Id, file)).ToArray();
    Check(imported.Select(d => d.FileType).SequenceEqual(["PDF", "DOCX", "TXT", "MD"]),
        "PDF/DOCX/TXT/MD 四种格式导入");
    Check(imported.All(d => d.KnowledgeBaseId == a.Id && d.Id != Guid.Empty) &&
        imported.Select(d => d.Id).Distinct().Count() == 4, "文档 ID 唯一且关联目标知识库");
    Check(imported.All(d => d.OriginalFileName.Contains(' ') && d.FileSize > 0 &&
        d.ParseStatus == ProcessingStatus.Pending && d.IndexStatus == ProcessingStatus.Pending),
        "中文/空格文件名与真实待解析、待索引状态");
    Check(imported.All(d => File.Exists(d.ManagedFilePath) &&
        File.ReadAllBytes(d.ManagedFilePath).SequenceEqual(File.ReadAllBytes(examples[Array.IndexOf(imported, d)])) &&
        d.ContentHash == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(d.ManagedFilePath))).ToLowerInvariant()),
        "受管理副本完整且 SHA-256 对应实际字节");
    var catalogText = File.ReadAllText(Path.Combine(paths.Databases, "documents.json"));
    Check(catalogText.Contains("\"document_id\"", StringComparison.Ordinal) &&
        catalogText.Contains("\"knowledge_base_id\"", StringComparison.Ordinal) &&
        catalogText.Contains("\"content_hash\"", StringComparison.Ordinal) &&
        catalogText.Contains("\"parse_status\": \"PENDING\"", StringComparison.Ordinal) &&
        catalogText.Contains("\"index_status\": \"PENDING\"", StringComparison.Ordinal),
        "版本化目录包含必要字段，解析与索引状态实际保存为 PENDING");
    Check(imported.All(d => d.ManagedFilePath.StartsWith(Path.Combine(paths.Documents, a.Id.ToString("N")) + Path.DirectorySeparatorChar,
        StringComparison.Ordinal) && !d.ManagedFilePath.Contains(d.OriginalFileName, StringComparison.Ordinal)),
        "受管理路径使用知识库 ID 与文档 ID，不依赖原文件名");
    var firstId = imported[0].Id;
    Reject<DuplicateDocumentException>(() => importer.Import(a.Id, examples[0]), "同一文件重复导入提示");
    var renamedSame = Source("完全不同的名字.PDF", File.ReadAllBytes(examples[0]));
    Reject<DuplicateDocumentException>(() => importer.Import(a.Id, renamedSame), "不同名相同内容按哈希识别重复");
    Check(repository.List(a.Id).Count == 4, "重复导入不增加记录");
    var sameNameOtherContent = Path.Combine(testRoot, "另一目录");
    Directory.CreateDirectory(sameNameOtherContent);
    var different = Path.Combine(sameNameOtherContent, "中文 PDF.pdf");
    File.WriteAllBytes(different, [0x25, 0x50, 0x44, 0x46, 0x09]);
    var otherContent = importer.Import(a.Id, different);
    Check(otherContent.OriginalFileName == imported[0].OriginalFileName && otherContent.Id != firstId &&
        otherContent.ContentHash != imported[0].ContentHash &&
        otherContent.ManagedFilePath != imported[0].ManagedFilePath, "同名不同内容分别保留、不覆盖");
    var cross = importer.Import(b.Id, examples[0]);
    Check(cross.ContentHash == imported[0].ContentHash && cross.ManagedFilePath != imported[0].ManagedFilePath &&
        cross.KnowledgeBaseId == b.Id, "不同知识库可导入相同内容并保持独立副本");
    Check(repository.List(a.Id).Count == 5 && repository.List(b.Id).Count == 1 &&
        repository.List(a.Id).All(d => d.KnowledgeBaseId == a.Id) &&
        repository.List(b.Id).All(d => d.KnowledgeBaseId == b.Id), "知识库 A/B 列表严格隔离");
    var longName = new string('长', 70) + " 名称.txt";
    var longDocument = importer.Import(a.Id, Source(longName, "长名内容"u8.ToArray()));
    Check(longDocument.OriginalFileName == longName && File.Exists(longDocument.ManagedFilePath),
        "较长中文文件名只存元数据，副本路径不受长度影响");
    var sourceGone = Source("将被删除.txt", "保留副本"u8.ToArray());
    var saved = importer.Import(a.Id, sourceGone);
    File.Delete(sourceGone);
    Check(File.ReadAllText(saved.ManagedFilePath) == "保留副本", "删除原始文件后受管理副本仍可用");
    var documentCount = repository.List(a.Id).Count;
    var filesBefore = Directory.GetFiles(Path.Combine(paths.Documents, a.Id.ToString("N"))).Order().ToArray();
    Reject<FileNotFoundException>(() => importer.Import(a.Id, Path.Combine(originalRoot, "不存在.pdf")), "不存在文件拒绝");
    Reject<NotSupportedException>(() => importer.Import(a.Id, Source("不支持.xlsx", [1, 2, 3])), "不支持格式拒绝");
    Reject<InvalidDataException>(() => importer.Import(a.Id, Source("空文件.txt", [])), "0 字节文件拒绝");
    Check(repository.List(a.Id).Count == documentCount &&
        filesBefore.SequenceEqual(Directory.GetFiles(Path.Combine(paths.Documents, a.Id.ToString("N"))).Order()),
        "无效导入不增加元数据或孤立文件");
    Reject<InvalidOperationException>(() => importer.Import(Guid.NewGuid(), Source("目标不存在.md", [7])), "目标知识库不存在拒绝");
    var testCopyFailure = new DocumentImportService(knowledgeBases, repository, paths, new FailingTransfer(false));
    Reject<IOException>(() => testCopyFailure.Import(a.Id, Source("复制失败.txt", [8])), "复制中途失败上报");
    var testPermissionFailure = new DocumentImportService(knowledgeBases, repository, paths, new FailingTransfer(true));
    Reject<UnauthorizedAccessException>(() => testPermissionFailure.Import(a.Id, Source("权限失败.txt", [9])), "读取权限失败上报");
    Check(filesBefore.SequenceEqual(Directory.GetFiles(Path.Combine(paths.Documents, a.Id.ToString("N"))).Order()) &&
        repository.List(a.Id).Count == documentCount, "复制/权限失败清理临时文件与记录");
    var beforeAddRepo = new FailingRepository(repository, false);
    var failedMetadata = new DocumentImportService(knowledgeBases, beforeAddRepo, paths);
    Reject<IOException>(() => failedMetadata.Import(a.Id, Source("元数据写入失败.txt", [10])), "元数据保存失败上报");
    Check(filesBefore.SequenceEqual(Directory.GetFiles(Path.Combine(paths.Documents, a.Id.ToString("N"))).Order()) &&
        repository.List(a.Id).Count == documentCount, "元数据失败移除已复制文件且无脏记录");
    var afterAddRepo = new FailingRepository(repository, true);
    var committed = new DocumentImportService(knowledgeBases, afterAddRepo, paths).Import(a.Id,
        Source("提交后回报失败.txt", [11]));
    Check(repository.List(a.Id).Any(d => d.Id == committed.Id) && File.Exists(committed.ManagedFilePath),
        "提交已成功但返回报错时回读确认，不误删有记录的副本");
    var lockPath = Path.Combine(paths.Databases, "knowledge-bases.json.lock");
    using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        Reject<IOException>(() => importer.Import(a.Id, Source("知识库锁.txt", [12])), "知识库并发锁阻止交叉删除/导入竞争");
    var documentLock = Path.Combine(paths.Databases, "documents.json.lock");
    filesBefore = Directory.GetFiles(Path.Combine(paths.Documents, a.Id.ToString("N"))).Order().ToArray();
    using (var held = new FileStream(documentLock, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        Reject<IOException>(() => importer.Import(a.Id, Source("文档锁.txt", [13])), "文档元数据锁冲突上报");
    Check(filesBefore.SequenceEqual(Directory.GetFiles(Path.Combine(paths.Documents, a.Id.ToString("N"))).Order()),
        "锁冲突不留下副本或临时文件");
    Reject<InvalidOperationException>(() => knowledgeBases.Delete(a.Id), "含文档知识库暂不允许删除");
    Check(knowledgeBases.List().Any(k => k.Id == a.Id) && repository.List(b.Id).Single().Id == cross.Id,
        "拒绝删除时 A/B 数据不变");
    var emptyBase = knowledgeBases.Create("空库可删"); knowledgeBases.Delete(emptyBase.Id);
    Check(knowledgeBases.List().All(k => k.Id != emptyBase.Id), "空知识库仍可删除");
    var process = new ProcessStartInfo(Environment.ProcessPath!) { RedirectStandardOutput = true, RedirectStandardError = true };
    if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!) == "dotnet")
        process.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    process.ArgumentList.Add("--probe"); process.ArgumentList.Add(basePath);
    process.ArgumentList.Add(a.Id.ToString()); process.ArgumentList.Add(repository.List(a.Id).Count.ToString());
    using (var child = Process.Start(process)!)
    {
        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        Check(child.WaitForExit(30000) && child.ExitCode == 0,
            "独立新进程重启后记录、副本与哈希一致：" + output + error);
    }
    var viewModel = new DocumentsViewModel(repository, importer);
    viewModel.SelectKnowledgeBase(b);
    Check(viewModel.Items.Count == 1 && viewModel.Items.Single().Name == cross.OriginalFileName &&
        viewModel.Items.Single().ParseStatus == "待解析" && viewModel.Items.Single().IndexStatus == "待索引",
        "UI 文档列表仅显示所选库并标记待处理");
    viewModel.SelectKnowledgeBase(a);
    Check(viewModel.Items.Count == repository.List(a.Id).Count, "UI 切换知识库重载本库列表");
    viewModel.SelectKnowledgeBase(null);
    Check(!viewModel.CanImport && viewModel.Items.Count == 0, "未选库禁止导入并清空列表");
    viewModel.SelectKnowledgeBase(b);
    await viewModel.ImportFilesAsync([examples[0], Path.Combine(originalRoot, "不存在.pdf")]);
    Check(viewModel.ImportSummary.Contains("重复 1", StringComparison.Ordinal) &&
        viewModel.ImportSummary.Contains("失败 1", StringComparison.Ordinal) &&
        repository.List(b.Id).Count == 1, "UI 批量导入逐文件反馈，失败不影响已有文件");
    var blockingTransfer = new BlockingTransfer();
    var switchingViewModel = new DocumentsViewModel(repository,
        new DocumentImportService(knowledgeBases, repository, paths, blockingTransfer));
    switchingViewModel.SelectKnowledgeBase(a);
    var switchingImport = switchingViewModel.ImportFilesAsync([Source("切换时导入.txt", [15])]);
    Check(blockingTransfer.Started.Wait(TimeSpan.FromSeconds(10)), "后台导入已开始");
    switchingViewModel.SelectKnowledgeBase(b);
    blockingTransfer.Release.Set();
    await switchingImport;
    Check(switchingViewModel.Items.Count == 1 &&
        switchingViewModel.Items.Single().Document.KnowledgeBaseId == b.Id &&
        switchingViewModel.ImportResults.Count == 0 &&
        switchingViewModel.ImportSummary == "" &&
        repository.List(a.Id).Any(d => d.OriginalFileName == "切换时导入.txt"),
        "导入 A 时切换到 B，不在 B 显示 A 的文件或导入结果");
    var stable = File.ReadAllBytes(Path.Combine(paths.Databases, "documents.json"));
    File.WriteAllText(Path.Combine(paths.Databases, "documents.json"), "{");
    Reject<InvalidDataException>(() => repository.List(a.Id), "损坏文档目录拒绝读取");
    Reject<InvalidDataException>(() => importer.Import(a.Id, Source("损坏目录拒绝写入.txt", [14])),
        "损坏文档目录拒绝导入");
    Check(File.ReadAllText(Path.Combine(paths.Databases, "documents.json")) == "{",
        "损坏元数据文件原样保留");
    File.WriteAllBytes(Path.Combine(paths.Databases, "documents.json"), stable);
    var moved = imported[0].ManagedFilePath + ".hidden";
    File.Move(imported[0].ManagedFilePath, moved);
    Reject<InvalidDataException>(() => repository.List(a.Id), "受管理副本被外部删除时检测不一致");
    File.Move(moved, imported[0].ManagedFilePath);
    Check(repository.List(a.Id).Count == documentCount + 2, "恢复受管理副本后元数据仍完整");
    Check(!Directory.GetFiles(Path.Combine(paths.Documents, a.Id.ToString("N")), "*.tmp").Any(),
        "正常与失败导入均无临时文件残留");
    Console.WriteLine($"完成 {checks} 项检查；Windows 文件选择器与真实拖拽未验证。");
    return 0;
}
finally { Directory.Delete(testRoot, true); }

sealed class FailingTransfer(bool permissionError) : IFileTransfer
{
    public (long Size, string Hash) CopyAndHash(string sourcePath, string stagingPath)
    {
        if (permissionError) throw new UnauthorizedAccessException("模拟无读取权限");
        File.WriteAllBytes(stagingPath, [1, 2]);
        throw new IOException("模拟复制中途失败");
    }
}
sealed class BlockingTransfer : IFileTransfer
{
    public ManualResetEventSlim Started { get; } = new(false);
    public ManualResetEventSlim Release { get; } = new(false);
    public (long Size, string Hash) CopyAndHash(string sourcePath, string stagingPath)
    {
        Started.Set();
        if (!Release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("测试同步超时");
        return new LocalFileTransfer().CopyAndHash(sourcePath, stagingPath);
    }
}
sealed class FailingRepository(IDocumentRepository inner, bool commitFirst) : IDocumentRepository
{
    public IReadOnlyList<Document> List(Guid id) => inner.List(id);
    public Document? Get(Guid id) => inner.Get(id);
    public bool HasDocuments(Guid id) => inner.HasDocuments(id);
    public void Add(Document document)
    {
        if (commitFirst) inner.Add(document);
        throw new IOException("模拟元数据保存错误");
    }
}

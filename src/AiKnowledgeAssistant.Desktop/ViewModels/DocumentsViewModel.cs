using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Core.KnowledgeBase;

namespace AiKnowledgeAssistant.Desktop.ViewModels;

public sealed class DocumentsViewModel : INotifyPropertyChanged
{
    private readonly IDocumentRepository repository;
    private readonly IDocumentImporter importer;
    private KnowledgeBase? selectedKnowledgeBase;
    private bool isBusy;
    private string importSummary = "";
    public ObservableCollection<DocumentListItem> Items { get; } = new();
    public ObservableCollection<string> ImportResults { get; } = new();
    public string SelectedKnowledgeBaseName => selectedKnowledgeBase?.Name ?? "未选择知识库";
    public bool HasKnowledgeBase => selectedKnowledgeBase is not null;
    public bool CanImport => HasKnowledgeBase && !IsBusy;
    public bool HasItems => Items.Count > 0;
    public bool IsBusy
    {
        get => isBusy;
        private set { isBusy = value; Changed(); Changed(nameof(CanImport)); }
    }
    public string ImportSummary
    {
        get => importSummary;
        private set { importSummary = value; Changed(); }
    }
    public DocumentsViewModel(IDocumentRepository repository, IDocumentImporter importer)
    {
        this.repository = repository;
        this.importer = importer;
    }

    public void SelectKnowledgeBase(KnowledgeBase? knowledgeBase)
    {
        selectedKnowledgeBase = knowledgeBase;
        Changed(nameof(SelectedKnowledgeBaseName));
        Changed(nameof(HasKnowledgeBase));
        Changed(nameof(CanImport));
        ImportSummary = "";
        ImportResults.Clear();
        Refresh();
    }

    public void Refresh()
    {
        if (selectedKnowledgeBase is null)
        {
            Items.Clear();
            Changed(nameof(HasItems));
            return;
        }
        try
        {
            var documents = repository.List(selectedKnowledgeBase.Id);
            Items.Clear();
            foreach (var document in documents) Items.Add(new DocumentListItem(document));
            Changed(nameof(HasItems));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ImportSummary = "无法读取文档列表。请检查本地数据后重试；现有列表已保留。";
        }
    }

    public async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        if (!CanImport) return;
        var files = paths.ToArray();
        if (files.Length == 0) return;
        var knowledgeBaseId = selectedKnowledgeBase!.Id;
        IsBusy = true;
        ImportResults.Clear();
        var succeeded = 0;
        var duplicated = 0;
        var failed = 0;
        try
        {
            foreach (var path in files)
            {
                var name = Path.GetFileName(path);
                try
                {
                    await Task.Run(() => importer.Import(knowledgeBaseId, path));
                    succeeded++;
                    if (selectedKnowledgeBase?.Id == knowledgeBaseId)
                        ImportResults.Add($"{name}：已导入，待解析、待索引");
                }
                catch (DuplicateDocumentException)
                {
                    duplicated++;
                    if (selectedKnowledgeBase?.Id == knowledgeBaseId)
                        ImportResults.Add($"{name}：该知识库已有相同内容，已跳过");
                }
                catch (Exception e)
                {
                    failed++;
                    if (selectedKnowledgeBase?.Id == knowledgeBaseId)
                        ImportResults.Add($"{name}：{SafeError(e)}");
                }
            }
            if (selectedKnowledgeBase?.Id == knowledgeBaseId)
            {
                ImportSummary = $"导入完成：成功 {succeeded}，重复 {duplicated}，失败 {failed}。";
                Refresh();
            }
        }
        finally { IsBusy = false; }
    }

    private static string SafeError(Exception e) => e switch
    {
        FileNotFoundException => "文件不存在或无法读取",
        UnauthorizedAccessException => "没有读取或写入权限",
        NotSupportedException => "不支持此格式，仅支持 PDF、DOCX、TXT、MD",
        InvalidDataException when e.Message.Contains("0 字节", StringComparison.Ordinal) => "文件为空（0 字节）",
        InvalidDataException => "本地文件或元数据异常，已停止导入",
        ArgumentException => "文件路径或名称无效",
        IOException when e.Message.Contains("无法确认导入状态", StringComparison.Ordinal) =>
            "无法确认导入状态，请刷新文件列表并检查本地数据后重试",
        IOException => "文件读写失败，请检查空间、权限或占用情况",
        InvalidOperationException => "目标知识库已不存在，或导入状态有冲突",
        _ => "导入失败，请稍后重试"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record DocumentListItem(Document Document)
{
    public string Name => Document.OriginalFileName;
    public string Type => Document.FileType;
    public string Size => Document.FileSize < 1024 ? $"{Document.FileSize} B" :
        Document.FileSize < 1024 * 1024 ? $"{Document.FileSize / 1024d:0.0} KB" :
        $"{Document.FileSize / (1024d * 1024d):0.0} MB";
    public string ImportedAt => Document.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string ParseStatus => "待解析";
    public string IndexStatus => "待索引";
}

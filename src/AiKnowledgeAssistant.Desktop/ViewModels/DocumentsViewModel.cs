using System.Data.Common;
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
    private readonly IDocumentParsingService? parser;
    private readonly IParsedContentRepository? parsed;
    private KnowledgeBase? selectedKnowledgeBase;
    private bool isBusy;
    private string importSummary = "";
    public ObservableCollection<DocumentListItem> Items { get; } = new();
    public ObservableCollection<string> ImportResults { get; } = new();
    public ObservableCollection<ParsedUnitListItem> ParsedUnits { get; } = new();
    public ObservableCollection<string> ParseFailures { get; } = new();
    private DocumentListItem? selectedDocument;
    public DocumentListItem? SelectedDocument
    {
        get => selectedDocument;
        set
        {
            if (selectedDocument == value) return;
            selectedDocument = value;
            Changed();
            Changed(nameof(CanParse));
            LoadParsed();
        }
    }
    public bool CanParse => selectedDocument is not null && !IsBusy && parser is not null &&
        selectedDocument.Document.ParseStatus != ProcessingStatus.Parsing;
    public string SelectedKnowledgeBaseName => selectedKnowledgeBase?.Name ?? "未选择知识库";
    public bool HasKnowledgeBase => selectedKnowledgeBase is not null;
    public bool CanImport => HasKnowledgeBase && !IsBusy;
    public bool HasItems => Items.Count > 0;
    public bool IsBusy
    {
        get => isBusy;
        private set { isBusy = value; Changed(); Changed(nameof(CanImport)); Changed(nameof(CanParse)); }
    }
    public string ImportSummary
    {
        get => importSummary;
        private set { importSummary = value; Changed(); }
    }
    public DocumentsViewModel(IDocumentRepository repository, IDocumentImporter importer,
        IDocumentParsingService? parser = null, IParsedContentRepository? parsed = null)
    {
        this.repository = repository;
        this.importer = importer;
        this.parser = parser;
        this.parsed = parsed;
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
            SelectedDocument = null;
            Items.Clear();
            Changed(nameof(HasItems));
            return;
        }
        try
        {
            var documents = repository.List(selectedKnowledgeBase.Id);
            var selectedId = SelectedDocument?.Document.Id;
            SelectedDocument = null;
            Items.Clear();
            foreach (var document in documents) Items.Add(new DocumentListItem(document));
            SelectedDocument = Items.FirstOrDefault(d => d.Document.Id == selectedId);
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
                    var imported = await Task.Run(() => importer.Import(knowledgeBaseId, path));
                    succeeded++;
                    if (selectedKnowledgeBase?.Id == knowledgeBaseId) Refresh();
                    var message = "已导入，待解析、待索引";
                    if (parser is not null)
                    {
                        try
                        {
                            var parsedDocument = await Task.Run(() => parser.Reparse(imported.Id));
                            message = parsedDocument.ParseStatus switch
                            {
                                ProcessingStatus.Completed => "已导入并解析，待索引",
                                ProcessingStatus.Partial => "已导入，部分页面解析失败，可重新解析",
                                _ => "已导入，解析失败，可重新解析"
                            };
                        }
                        catch (Exception e)
                        {
                            message = "已导入，但解析未完成：" + SafeError(e);
                        }
                    }
                    if (selectedKnowledgeBase?.Id == knowledgeBaseId)
                        ImportResults.Add($"{name}：{message}");
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

    public async Task ParseSelectedAsync()
    {
        if (!CanParse) return;
        var documentId = SelectedDocument!.Document.Id;
        var knowledgeBaseId = selectedKnowledgeBase!.Id;
        IsBusy = true;
        try
        {
            var updated = await Task.Run(() => parser!.Reparse(documentId));
            if (selectedKnowledgeBase?.Id == knowledgeBaseId)
            {
                Refresh();
                ImportSummary = updated.ParseStatus switch
                {
                    ProcessingStatus.Completed => "重新解析完成；索引尚未建立。",
                    ProcessingStatus.Partial => "部分内容解析成功，失败页见下方。",
                    _ => "解析失败，原因见下方；受管理原文件已保留。"
                };
            }
        }
        catch (Exception e)
        {
            if (selectedKnowledgeBase?.Id == knowledgeBaseId)
            {
                ImportSummary = "重新解析未完成：" + SafeError(e);
                Refresh();
            }
        }
        finally { IsBusy = false; }
    }

    private void LoadParsed()
    {
        ParsedUnits.Clear();
        ParseFailures.Clear();
        if (SelectedDocument is null || parsed is null) return;
        try
        {
            var result = parsed.GetParsed(SelectedDocument.Document.Id);
            if (result is null) return;
            foreach (var unit in result.Units) ParsedUnits.Add(new ParsedUnitListItem(unit));
            foreach (var failure in result.Failures)
                ParseFailures.Add(failure.PageNumber is int page
                    ? $"PDF 第 {page} 页：{failure.Reason}" : failure.Reason);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ParseFailures.Add("无法读取已保存的解析结果，请检查本地数据。");
        }
    }

    private static string SafeError(Exception e) => e is DbException
        ? "SQLite 数据库读写失败，请检查磁盘空间与权限" : e switch
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
    public string ParseStatus => Document.ParseStatus switch
    {
        ProcessingStatus.Pending => "待解析",
        ProcessingStatus.Parsing => "正在解析",
        ProcessingStatus.Completed => "已完成",
        ProcessingStatus.Partial => "部分完成",
        _ => "解析失败"
    };
    public string ParseError => Document.ParseError ?? "";
    public string IndexStatus => "待索引";
}

public sealed record ParsedUnitListItem(ParsedUnit Unit)
{
    public string Position => Unit.PageNumber is int page ? $"PDF 第 {page} 页" :
        Unit.ParserType == "DOCX" ?
            $"{(Unit.SectionPath ?? "无标题章节")} · 段落 {Unit.ParagraphNumber}" :
            $"{(Unit.SectionPath ?? "无标题章节")} · 第 {Unit.StartLine}-{Unit.EndLine} 行";
    public string Source => Unit.SourceType == SourceType.Ocr ? "OCR（机器识别）" : "TEXT";
    public string Preview => Unit.Text.Length > 200 ? Unit.Text[..200] + "…" : Unit.Text;
}

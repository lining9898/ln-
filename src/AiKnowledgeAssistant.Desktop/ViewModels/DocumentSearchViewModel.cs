using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Runtime.CompilerServices;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Core.Retrieval;
using AiKnowledgeAssistant.Core.Sources;

namespace AiKnowledgeAssistant.Desktop.ViewModels;

public sealed class DocumentSearchViewModel : INotifyPropertyChanged
{
    private readonly IDocumentSearch search;
    private readonly Func<Guid?> selectedKnowledgeBase;
    private readonly ISourceViewer? sourceViewer;
    private string query = "";
    private string message = "请选择知识库并输入搜索词。";
    private SearchHitListItem? selectedResult;
    private SourceView? source;
    private string jumpPage = "";
    public ObservableCollection<SearchHitListItem> Results { get; } = new();
    public string Query { get => query; set { query = value; Changed(); } }
    public string Message { get => message; private set { message = value; Changed(); } }
    public SearchHitListItem? SelectedResult
    {
        get => selectedResult;
        set { selectedResult = value; Changed(); Changed(nameof(CanOpenSource)); }
    }
    public SourceView? Source
    {
        get => source;
        private set
        {
            source = value;
            JumpPage = value?.CurrentPage?.ToString() ?? "";
            Changed();
            Changed(nameof(SourceTitle));
            Changed(nameof(SourcePosition));
            Changed(nameof(SourceText));
            Changed(nameof(SourcePath));
            Changed(nameof(HasSource));
        }
    }
    public string JumpPage { get => jumpPage; set { jumpPage = value; Changed(); } }
    public bool CanOpenSource => sourceViewer is not null && SelectedResult is not null;
    public bool HasSource => Source is not null;
    public string SourceTitle => Source is null ? "未打开来源" : Source.FileName;
    public string SourcePosition => Source?.Position ?? "请选择搜索结果并打开来源。";
    public string SourceText => Source?.Text ?? "";
    public string SourcePath => Source?.ManagedFilePath ?? "";
    public RelayCommand SearchCommand { get; }
    public RelayCommand RebuildCommand { get; }
    public RelayCommand OpenSourceCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand JumpPageCommand { get; }

    public DocumentSearchViewModel(IDocumentSearch search, Func<Guid?> selectedKnowledgeBase,
        ISourceViewer? sourceViewer = null)
    {
        this.search = search;
        this.selectedKnowledgeBase = selectedKnowledgeBase;
        this.sourceViewer = sourceViewer;
        SearchCommand = new(Search);
        RebuildCommand = new(Rebuild);
        OpenSourceCommand = new(OpenSource);
        PreviousPageCommand = new(() => MovePage(-1));
        NextPageCommand = new(() => MovePage(1));
        JumpPageCommand = new(JumpToPage);
    }

    public void ClearResults()
    {
        Results.Clear();
        SelectedResult = null;
        Source = null;
        Message = "知识库已切换，请输入搜索词。";
    }

    private void Search()
    {
        Results.Clear();
        var kb = selectedKnowledgeBase();
        if (kb is null) { Message = "请先选择知识库。"; return; }
        if (string.IsNullOrWhiteSpace(Query)) { Message = "请输入搜索词。"; return; }
        try
        {
            foreach (var hit in search.Search(Query, [kb.Value])) Results.Add(new SearchHitListItem(hit, Query));
            SelectedResult = Results.FirstOrDefault();
            Message = Results.Count == 0 ? "没有找到匹配的原文。" : $"找到 {Results.Count} 条原文结果。";
        }
        catch (Exception e) when (e is DbException or ArgumentException or InvalidDataException)
        { Message = "搜索失败，请检查搜索词或本地索引。"; }
    }

    private void Rebuild()
    {
        var kb = selectedKnowledgeBase();
        if (kb is null) { Message = "请先选择知识库。"; return; }
        try
        {
            search.RebuildIndex(kb);
            var audit = search.AuditIndex();
            Message = audit.IsConsistent ? "索引已重建并通过一致性检查。" : "索引已重建，但一致性检查仍发现异常。";
            Results.Clear();
            SelectedResult = null;
            Source = null;
        }
        catch (Exception e) when (e is DbException or InvalidDataException)
        { Message = "索引重建失败，原始解析内容已保留。"; }
    }

    private void OpenSource()
    {
        if (sourceViewer is null || SelectedResult is null) return;
        try
        {
            Source = sourceViewer.Open(SelectedResult.Hit);
            Message = "已打开来源，可核对原文位置。";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentOutOfRangeException)
        { Message = "无法打开来源，请检查本地原文件和解析结果。"; }
    }

    private void MovePage(int delta)
    {
        if (sourceViewer is null || Source?.CurrentPage is not int current || Source.PageCount is null) return;
        var target = Math.Clamp(current + delta, 1, Source.PageCount.Value);
        try { Source = sourceViewer.Jump(Source.DocumentId, target); }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentOutOfRangeException)
        { Message = "页码跳转失败，请检查 PDF 原文件。"; }
    }

    private void JumpToPage()
    {
        if (sourceViewer is null || Source is null || !int.TryParse(JumpPage, out var page)) return;
        try { Source = sourceViewer.Jump(Source.DocumentId, page); }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or ArgumentOutOfRangeException)
        { Message = "页码跳转失败，请输入有效物理页码。"; }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record SearchHitListItem(SearchHit Hit, string Query)
{
    public string FileName => Hit.FileName;
    public string Position => Hit.PageNumber is int page ? $"PDF 物理第 {page} 页" :
        Hit.ParagraphNumber is int paragraph ? $"{Hit.SectionPath ?? Hit.SectionTitle ?? "无标题章节"} · 段落 {paragraph}" :
        Hit.StartLine is int start ? $"{Hit.SectionPath ?? Hit.SectionTitle ?? "无标题章节"} · 第 {start}–{Hit.EndLine} 行" : "来源位置已记录";
    public string Source => Hit.SourceType == SourceType.Ocr ? "OCR（机器识别，未经人工核验）" : "TEXT（提取文本）";
    public string Text
    {
        get
        {
            var term = Query.Trim().Trim('"', '“', '”');
            var position = Hit.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (position < 0)
                position = Query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => Hit.Text.IndexOf(part.Trim('"', '“', '”'), StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(i => i >= 0, 0);
            var start = Math.Max(0, position - 100);
            var length = Math.Min(500, Hit.Text.Length - start);
            return (start > 0 ? "…" : "") + Hit.Text.Substring(start, length) +
                (start + length < Hit.Text.Length ? "…" : "");
        }
    }
}

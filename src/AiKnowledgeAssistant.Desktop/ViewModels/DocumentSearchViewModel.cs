using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data.Common;
using System.Runtime.CompilerServices;
using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Core.Retrieval;

namespace AiKnowledgeAssistant.Desktop.ViewModels;

public sealed class DocumentSearchViewModel : INotifyPropertyChanged
{
    private readonly IDocumentSearch search;
    private readonly Func<Guid?> selectedKnowledgeBase;
    private string query = "";
    private string message = "请选择知识库并输入搜索词。";
    public ObservableCollection<SearchHitListItem> Results { get; } = new();
    public string Query { get => query; set { query = value; Changed(); } }
    public string Message { get => message; private set { message = value; Changed(); } }
    public RelayCommand SearchCommand { get; }
    public RelayCommand RebuildCommand { get; }

    public DocumentSearchViewModel(IDocumentSearch search, Func<Guid?> selectedKnowledgeBase)
    {
        this.search = search;
        this.selectedKnowledgeBase = selectedKnowledgeBase;
        SearchCommand = new(Search);
        RebuildCommand = new(Rebuild);
    }

    public void ClearResults()
    {
        Results.Clear();
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
        }
        catch (Exception e) when (e is DbException or InvalidDataException)
        { Message = "索引重建失败，原始解析内容已保留。"; }
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

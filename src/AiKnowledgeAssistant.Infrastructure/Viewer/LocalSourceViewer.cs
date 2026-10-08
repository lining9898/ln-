using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Core.Retrieval;
using AiKnowledgeAssistant.Core.Sources;
using UglyToad.PdfPig;

namespace AiKnowledgeAssistant.Infrastructure.Viewer;

public sealed class LocalSourceViewer(IDocumentRepository documents, IParsedContentRepository parsed) : ISourceViewer
{
    public SourceView Open(SearchHit hit)
    {
        var document = documents.Get(hit.DocumentId) ??
            throw new InvalidOperationException("文档不存在，请刷新列表。");
        if (document.KnowledgeBaseId != hit.KnowledgeBaseId)
            throw new InvalidOperationException("来源与文档所属知识库不一致。");
        if (document.FileType == "PDF")
            return PdfPage(document, hit.ContentId, hit.PageNumber ?? 1);
        var unit = parsed.GetParsed(document.Id)?.Units.FirstOrDefault(u => u.DocumentId == document.Id && u.Text == hit.Text) ??
            parsed.GetParsed(document.Id)?.Units.FirstOrDefault(u => u.DocumentId == document.Id);
        return TextSource(document, hit.ContentId, unit, hit.Text);
    }

    public SourceView Open(Guid documentId, int? pageNumber = null)
    {
        var document = documents.Get(documentId) ??
            throw new InvalidOperationException("文档不存在，请刷新列表。");
        if (document.FileType == "PDF") return PdfPage(document, null, pageNumber ?? 1);
        var unit = parsed.GetParsed(document.Id)?.Units.FirstOrDefault();
        return TextSource(document, null, unit, unit?.Text ?? "");
    }

    public SourceView Jump(Guid documentId, int pageNumber)
    {
        var document = documents.Get(documentId) ??
            throw new InvalidOperationException("文档不存在，请刷新列表。");
        if (document.FileType != "PDF") throw new InvalidOperationException("只有 PDF 支持页码跳转。");
        return PdfPage(document, null, pageNumber);
    }

    private SourceView PdfPage(Document document, Guid? contentId, int pageNumber)
    {
        using var pdf = PdfDocument.Open(document.ManagedFilePath);
        if (pageNumber < 1 || pageNumber > pdf.NumberOfPages)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "页码超出 PDF 范围。");
        var page = pdf.GetPage(pageNumber);
        var text = page.Text;
        var unit = parsed.GetParsed(document.Id)?.Units
            .Where(u => u.PageNumber == pageNumber)
            .OrderBy(u => u.Sequence)
            .FirstOrDefault();
        if (unit?.SourceType == SourceType.Ocr && !string.IsNullOrWhiteSpace(unit.Text))
            text = unit.Text;
        else if (string.IsNullOrWhiteSpace(text))
        {
            text = unit?.Text ?? "此页没有可提取文本；如为扫描页，请先完成 OCR。";
        }
        return new SourceView(document.KnowledgeBaseId, document.Id, contentId, document.OriginalFileName,
            document.ManagedFilePath, document.FileType, pageNumber, pdf.NumberOfPages,
            $"PDF 物理第 {pageNumber} 页 / 共 {pdf.NumberOfPages} 页", text);
    }

    private SourceView TextSource(Document document, Guid? contentId, ParsedUnit? unit, string fallbackText)
    {
        var position = unit is null ? "来源位置已记录" :
            unit.ParagraphNumber is int paragraph ? $"{unit.SectionPath ?? unit.SectionTitle ?? "无标题章节"} · 段落 {paragraph}" :
            unit.StartLine is int start ? $"{unit.SectionPath ?? unit.SectionTitle ?? "无标题章节"} · 第 {start}-{unit.EndLine} 行" :
            "来源位置已记录";
        return new SourceView(document.KnowledgeBaseId, document.Id, contentId, document.OriginalFileName,
            document.ManagedFilePath, document.FileType, null, null, position, unit?.Text ?? fallbackText);
    }
}

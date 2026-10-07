using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Infrastructure.OCR;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace AiKnowledgeAssistant.Infrastructure.Parser;

public sealed class PdfDocumentParser(IPdfPageOcr ocr) : IDocumentParser
{
    public string ParserType => "PDF";
    public bool Supports(string fileType) => fileType == "PDF";

    public ParsedDocument Parse(Document document)
    {
        using var pdf = PdfDocument.Open(document.ManagedFilePath);
        var units = new List<ParsedUnit>();
        var failures = new List<ParseFailure>();
        for (var pageNumber = 1; pageNumber <= pdf.NumberOfPages; pageNumber++)
        {
            try
            {
                var page = pdf.GetPage(pageNumber);
                var text = ContentOrderTextExtractor.GetText(page).Trim();
                var sourceType = SourceType.Text;
                if (!text.Any(char.IsLetterOrDigit))
                {
                    if (!page.GetImages().Any()) continue; // Blank page: no false OCR failure.
                    try
                    {
                        text = ocr.Recognize(document.ManagedFilePath, pageNumber).Trim();
                        sourceType = SourceType.Ocr;
                    }
                    catch (Exception e) when (e is IOException or InvalidOperationException or
                        TimeoutException or System.ComponentModel.Win32Exception)
                    {
                        failures.Add(new ParseFailure(pageNumber, "本地 OCR 工具不可用或识别失败。"));
                        continue;
                    }
                    if (!text.Any(char.IsLetterOrDigit))
                    {
                        failures.Add(new ParseFailure(pageNumber, "OCR 未得到可用文本。"));
                        continue;
                    }
                }
                units.Add(new ParsedUnit(document.Id, document.KnowledgeBaseId, units.Count + 1,
                    text, sourceType, pageNumber, null, null, null, null,
                    ParserType, DateTimeOffset.UtcNow));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                failures.Add(new ParseFailure(pageNumber, "此页文本提取失败。"));
            }
        }
        return new ParsedDocument(document.Id, document.KnowledgeBaseId, ParserType,
            pdf.NumberOfPages, units, failures, DateTimeOffset.UtcNow);
    }
}

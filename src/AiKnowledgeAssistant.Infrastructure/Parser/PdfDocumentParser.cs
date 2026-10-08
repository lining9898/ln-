using AiKnowledgeAssistant.Core.Documents;
using AiKnowledgeAssistant.Infrastructure.OCR;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using System.Text;
using System.Text.RegularExpressions;

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
                if (!HasUsefulExtractedText(text))
                {
                    if (!page.GetImages().Any())
                    {
                        if (text.Any(char.IsLetterOrDigit)) goto AddUnit;
                        continue; // Blank page: no false OCR failure.
                    }
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
                AddUnit:
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

    private static bool HasUsefulExtractedText(string text)
    {
        if (!text.Any(char.IsLetterOrDigit)) return false;
        var cleaned = Regex.Replace(text, @"https?://\S+|www\.\S+", "", RegexOptions.IgnoreCase);
        cleaned = cleaned.Replace("标准分享吧", "", StringComparison.OrdinalIgnoreCase);
        var lettersOrDigits = 0;
        var cjk = 0;
        foreach (var rune in cleaned.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune)) lettersOrDigits++;
            if (rune.Value is >= 0x3400 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF or
                >= 0x20000 and <= 0x2FA1F) cjk++;
        }
        return cjk >= 12 || lettersOrDigits >= 80;
    }
}

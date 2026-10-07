using System.Text.RegularExpressions;
using AiKnowledgeAssistant.Core.Documents;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AiKnowledgeAssistant.Infrastructure.Parser;

public sealed partial class DocxDocumentParser : IDocumentParser
{
    public string ParserType => "DOCX";
    public bool Supports(string fileType) => fileType == "DOCX";
    [GeneratedRegex(@"^Heading([123])$", RegexOptions.IgnoreCase)]
    private static partial Regex HeadingPattern();

    public ParsedDocument Parse(AiKnowledgeAssistant.Core.Documents.Document document)
    {
        using var word = WordprocessingDocument.Open(document.ManagedFilePath, false);
        var body = word.MainDocumentPart?.Document?.Body
            ?? throw new InvalidDataException("DOCX 正文不存在。");
        var units = new List<ParsedUnit>();
        var headings = new string?[3];
        var position = 0;
        string? Title() => headings.LastOrDefault(h => h is not null);
        string? PathText() => headings.Any(h => h is not null)
            ? string.Join(" > ", headings.Where(h => h is not null)) : null;
        void Add(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            units.Add(new ParsedUnit(document.Id, document.KnowledgeBaseId, units.Count + 1,
                text, SourceType.Text, null, Title(), PathText(), null, null,
                ParserType, DateTimeOffset.UtcNow, position));
        }
        foreach (var child in body.ChildElements)
        {
            if (child is Paragraph paragraph)
            {
                position++;
                var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text)).Trim();
                var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
                var match = HeadingPattern().Match(style);
                if (match.Success && !string.IsNullOrWhiteSpace(text))
                {
                    var level = int.Parse(match.Groups[1].Value);
                    headings[level - 1] = text;
                    for (var h = level; h < headings.Length; h++) headings[h] = null;
                }
                Add(text);
            }
            else if (child is Table table)
            {
                foreach (var row in table.Elements<TableRow>())
                {
                    position++;
                    var cells = row.Elements<TableCell>().Select(cell =>
                        string.Join(" / ", cell.Descendants<Paragraph>().Select(p =>
                            string.Concat(p.Descendants<Text>().Select(t => t.Text)).Trim())
                            .Where(t => t.Length > 0)));
                    Add(string.Join(" | ", cells));
                }
            }
        }
        return new ParsedDocument(document.Id, document.KnowledgeBaseId, ParserType, null,
            units, Array.Empty<ParseFailure>(), DateTimeOffset.UtcNow);
    }
}

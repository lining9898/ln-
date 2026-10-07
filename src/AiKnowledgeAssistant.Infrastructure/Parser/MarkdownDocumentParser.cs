using System.Text.RegularExpressions;
using AiKnowledgeAssistant.Core.Documents;

namespace AiKnowledgeAssistant.Infrastructure.Parser;

public sealed partial class MarkdownDocumentParser : IDocumentParser
{
    public string ParserType => "MARKDOWN";
    public bool Supports(string fileType) => fileType == "MD";

    [GeneratedRegex(@"^\s{0,3}(#{1,6})[ \t]+(.+?)\s*#*\s*$")]
    private static partial Regex HeadingPattern();

    public ParsedDocument Parse(Document document)
    {
        var lines = ParserText.Lines(ParserText.ReadText(document.ManagedFilePath));
        var units = new List<ParsedUnit>();
        var headings = new string?[6];
        var paragraph = new List<string>();
        var startLine = 0;
        var inCodeFence = false;
        string? SectionTitle() => headings.LastOrDefault(x => x is not null);
        string? SectionPath() => headings.Any(x => x is not null)
            ? string.Join(" > ", headings.Where(x => x is not null)) : null;
        void Flush(int endLine)
        {
            if (paragraph.Count == 0) return;
            units.Add(new ParsedUnit(document.Id, document.KnowledgeBaseId, units.Count + 1,
                string.Join("\n", paragraph), SourceType.Text, null, SectionTitle(), SectionPath(),
                startLine, endLine, ParserType, DateTimeOffset.UtcNow));
            paragraph.Clear();
        }
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                if (paragraph.Count == 0) startLine = i + 1;
                paragraph.Add(line);
                inCodeFence = !inCodeFence;
                continue;
            }
            var heading = inCodeFence ? Match.Empty : HeadingPattern().Match(line);
            if (heading.Success)
            {
                Flush(i);
                var level = heading.Groups[1].Value.Length;
                headings[level - 1] = heading.Groups[2].Value.Trim();
                for (var h = level; h < headings.Length; h++) headings[h] = null;
                units.Add(new ParsedUnit(document.Id, document.KnowledgeBaseId, units.Count + 1,
                    line, SourceType.Text, null, SectionTitle(), SectionPath(),
                    i + 1, i + 1, ParserType, DateTimeOffset.UtcNow));
                continue;
            }
            if (!inCodeFence && string.IsNullOrWhiteSpace(line)) { Flush(i); continue; }
            if (paragraph.Count == 0) startLine = i + 1;
            paragraph.Add(line);
        }
        Flush(lines.Length);
        return new ParsedDocument(document.Id, document.KnowledgeBaseId, ParserType, null,
            units, Array.Empty<ParseFailure>(), DateTimeOffset.UtcNow);
    }
}

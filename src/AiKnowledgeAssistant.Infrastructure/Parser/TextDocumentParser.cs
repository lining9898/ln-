using AiKnowledgeAssistant.Core.Documents;

namespace AiKnowledgeAssistant.Infrastructure.Parser;

public sealed class TextDocumentParser : IDocumentParser
{
    public string ParserType => "TXT";
    public bool Supports(string fileType) => fileType == "TXT";

    public ParsedDocument Parse(Document document)
    {
        var lines = ParserText.Lines(ParserText.ReadText(document.ManagedFilePath));
        var units = new List<ParsedUnit>();
        var paragraph = new List<string>();
        var startLine = 0;
        void Flush(int endLine)
        {
            if (paragraph.Count == 0) return;
            units.Add(new ParsedUnit(document.Id, document.KnowledgeBaseId, units.Count + 1,
                string.Join("\n", paragraph), SourceType.Text, null, null, null,
                startLine, endLine, ParserType, DateTimeOffset.UtcNow));
            paragraph.Clear();
        }
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) { Flush(i); continue; }
            if (paragraph.Count == 0) startLine = i + 1;
            paragraph.Add(lines[i]);
        }
        Flush(lines.Length);
        return new ParsedDocument(document.Id, document.KnowledgeBaseId, ParserType, null,
            units, Array.Empty<ParseFailure>(), DateTimeOffset.UtcNow);
    }
}

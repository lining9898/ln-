using System.Text;

namespace AiKnowledgeAssistant.Infrastructure.Retrieval;

// FTS5 unicode61 does not split contiguous Chinese text into useful words.
// Index CJK single characters and adjacent pairs; keep Latin/number runs as terms.
internal static class SearchTerms
{
    public static string Build(string text)
    {
        var terms = new List<string>();
        var run = new StringBuilder();
        Rune? previousCjk = null;
        foreach (var rune in text.Normalize(NormalizationForm.FormKC).EnumerateRunes())
        {
            var cjk = IsCjk(rune);
            if (cjk)
            {
                Flush();
                terms.Add(rune.ToString());
                if (previousCjk is Rune prior) terms.Add(prior.ToString() + rune);
                previousCjk = rune;
            }
            else if (Rune.IsLetterOrDigit(rune))
            {
                previousCjk = null;
                run.Append(rune.ToString().ToLowerInvariant());
            }
            else
            {
                Flush();
                previousCjk = null;
            }
        }
        Flush();
        return string.Join(' ', terms);
        void Flush() { if (run.Length == 0) return; terms.Add(run.ToString()); run.Clear(); }
    }

    public static string MatchExpression(string query)
    {
        var terms = Build(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal);
        // Quote every generated token so user supplied FTS operators never become SQL syntax.
        return string.Join(" AND ", terms.Select(t => "\"" + t.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""));
    }

    public static IReadOnlyList<string> ExactPhrases(string query)
    {
        var phrases = new List<string>();
        for (var i = 0; i < query.Length; i++)
        {
            if (query[i] != '"' && query[i] != '“') continue;
            var close = query[i] == '“' ? '”' : '"';
            var end = query.IndexOf(close, i + 1);
            if (end < 0) break;
            var phrase = query[(i + 1)..end].Trim();
            if (phrase.Length > 0) phrases.Add(phrase);
            i = end;
        }
        return phrases;
    }

    private static bool IsCjk(Rune r) =>
        r.Value is >= 0x3400 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF or
            >= 0x20000 and <= 0x2FA1F or >= 0x3040 and <= 0x30FF or
            >= 0xAC00 and <= 0xD7AF;
}

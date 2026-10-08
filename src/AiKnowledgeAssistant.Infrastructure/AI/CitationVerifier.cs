using System.Text.RegularExpressions;
using AiKnowledgeAssistant.Core.AI;
using AiKnowledgeAssistant.Core.Sources;

namespace AiKnowledgeAssistant.Infrastructure.AI;

public sealed partial class CitationVerifier(ISourceViewer sourceViewer) : ICitationVerifier
{
    public CitationVerification Verify(RagAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        var citedIds = SourceReference().Matches(answer.Answer)
            .Select(m => "S" + m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (citedIds.Length == 0 && answer.UsedAi)
            return new CitationVerification(false, ["NO_CITATION"], []);
        var citations = answer.Citations.ToDictionary(c => c.SourceId, StringComparer.Ordinal);
        var missing = citedIds.Where(id => !citations.ContainsKey(id)).ToArray();
        var verified = new List<VerifiedCitation>();
        foreach (var id in citedIds)
        {
            if (!citations.TryGetValue(id, out var citation)) continue;
            verified.Add(new VerifiedCitation(id, sourceViewer.Open(citation.Hit)));
        }
        return new CitationVerification(missing.Length == 0, missing, verified);
    }

    [GeneratedRegex(@"\[S(\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex SourceReference();
}

namespace AiKnowledgeAssistant.Core.Embedding;

public interface ITextEmbedder
{
    string ModelId { get; }
    int Dimension { get; }
    float[] EmbedPassage(string text);
    float[] EmbedQuery(string text);
}

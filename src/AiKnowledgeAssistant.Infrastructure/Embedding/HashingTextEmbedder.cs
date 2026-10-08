using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AiKnowledgeAssistant.Core.Embedding;

namespace AiKnowledgeAssistant.Infrastructure.Embedding;

public sealed class HashingTextEmbedder : ITextEmbedder
{
    public string ModelId => "local-hashing-multilingual-v1";
    public int Dimension => 384;

    public float[] EmbedPassage(string text) => Embed(text);
    public float[] EmbedQuery(string text) => Embed(text);

    private float[] Embed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var vector = new float[Dimension];
        foreach (var token in Tokens(text))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            var slot = BitConverter.ToUInt32(hash, 0) % (uint)Dimension;
            var sign = (hash[4] & 1) == 0 ? 1f : -1f;
            var weight = token.Length == 1 ? 0.75f : 1f;
            vector[slot] += sign * weight;
        }
        Normalize(vector);
        return vector;
    }

    private static IEnumerable<string> Tokens(string text)
    {
        var latin = new StringBuilder();
        var previousChinese = "";
        foreach (var rune in text.EnumerateRunes())
        {
            if (IsChinese(rune))
            {
                foreach (var token in FlushLatin()) yield return token;
                var current = rune.ToString();
                yield return current;
                if (previousChinese.Length > 0) yield return previousChinese + current;
                previousChinese = current;
                continue;
            }
            previousChinese = "";
            if (Rune.IsLetterOrDigit(rune))
            {
                latin.Append(rune.ToString().ToLowerInvariant());
                continue;
            }
            foreach (var token in FlushLatin()) yield return token;
        }
        foreach (var token in FlushLatin()) yield return token;

        IEnumerable<string> FlushLatin()
        {
            if (latin.Length == 0) yield break;
            var token = latin.ToString();
            latin.Clear();
            yield return token;
            if (token.Length > 4)
            {
                for (var i = 0; i <= token.Length - 3; i++) yield return token.Substring(i, 3);
            }
        }
    }

    private static bool IsChinese(Rune rune)
    {
        var value = rune.Value;
        return value is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF;
    }

    private static void Normalize(float[] vector)
    {
        var sum = 0d;
        foreach (var value in vector) sum += value * value;
        var norm = Math.Sqrt(sum);
        if (norm <= 0) return;
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / norm);
    }
}

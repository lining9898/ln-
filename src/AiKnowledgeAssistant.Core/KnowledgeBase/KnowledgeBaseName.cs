using System.Text;

namespace AiKnowledgeAssistant.Core.KnowledgeBase;

public static class KnowledgeBaseName
{
    public const int MaxLength = 80;

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("知识库名称不能为空。");
        var normalized = name.Trim().Normalize(NormalizationForm.FormC);
        if (normalized.EnumerateRunes().Count() > MaxLength)
            throw new ArgumentException($"知识库名称不能超过 {MaxLength} 个字符。");
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("知识库名称不能包含换行或控制字符。");
        return normalized;
    }
}

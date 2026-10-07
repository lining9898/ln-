using System.Text;

namespace AiKnowledgeAssistant.Infrastructure.Parser;

internal static class ParserText
{
    static ParserText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding encoding = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? new UTF8Encoding(false, true) :
            bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }) ? new UnicodeEncoding(false, true, true) :
            bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }) ? new UnicodeEncoding(true, true, true) :
            new UTF8Encoding(false, true);
        string text;
        try { text = encoding.GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException)
        {
            try { text = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback,
                DecoderFallback.ExceptionFallback).GetString(bytes); }
            catch (DecoderFallbackException e)
            {
                throw new InvalidDataException("文本编码无法识别，请转换为 UTF-8 或 GB18030 后重新导入。", e);
            }
        }
        if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw new InvalidDataException("文本包含不可读取的控制字符。");
        return text;
    }

    public static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n').Split('\n');
}

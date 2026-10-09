using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiKnowledgeAssistant.Core.AI;

namespace AiKnowledgeAssistant.Infrastructure.AI;

public sealed class TavilyWebSearch(HttpClient http, ICredentialStore credentials) : IWebSearch
{
    public const string CredentialTarget = "AIKnowledgeAssistant/Tavily/APIKey";

    public async Task<IReadOnlyList<WebSearchHit>> SearchAsync(string publicQuery, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(publicQuery) || publicQuery.Length > 300)
            throw new WebSearchException("请输入不超过 300 字的公开搜索词。");
        var key = credentials.ReadSecret(CredentialTarget);
        if (string.IsNullOrWhiteSpace(key)) throw new WebSearchException("请先在设置中保存 Tavily 搜索 Key。");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(new { query = publicQuery.Trim(), search_depth = "basic",
            max_results = 5, include_answer = false, include_raw_content = false, include_published_date = true });
        try
        {
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new WebSearchException((int)response.StatusCode switch
                {
                    401 or 403 => "Tavily 搜索 Key 无效或无访问权限。",
                    402 or 432 or 433 => "Tavily 搜索额度不足，请检查账户额度。",
                    429 => "联网搜索请求过于频繁，请稍后再试。",
                    _ => "联网搜索服务暂时不可用，请稍后重试或关闭联网搜索。"
                });
            using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (!payload.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                throw new WebSearchException("联网搜索返回格式异常。");
            var hits = new List<WebSearchHit>();
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in results.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                string Get(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "" : "";
                var url = Get("url");
                var content = Get("content");
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") ||
                    uri.IsLoopback || uri.UserInfo.Length > 0 || string.IsNullOrWhiteSpace(content) || !urls.Add(uri.AbsoluteUri)) continue;
                if (url.Length > 2048) continue;
                var title = Get("title");
                var date = Get("published_date");
                hits.Add(new WebSearchHit(title[..Math.Min(200, title.Length)], uri.AbsoluteUri,
                    content[..Math.Min(900, content.Length)], string.IsNullOrWhiteSpace(date) ? "发布日期未提供" : date[..Math.Min(80, date.Length)]));
                if (hits.Count == 5) break;
            }
            return hits;
        }
        catch (HttpRequestException) { throw new WebSearchException("联网搜索连接失败，请检查网络。"); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new WebSearchException("联网搜索超时，请稍后重试。"); }
        catch (JsonException) { throw new WebSearchException("联网搜索返回格式异常。"); }
    }
}

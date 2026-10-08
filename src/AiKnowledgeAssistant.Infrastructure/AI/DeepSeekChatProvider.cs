using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiKnowledgeAssistant.Core.AI;

namespace AiKnowledgeAssistant.Infrastructure.AI;

public sealed record DeepSeekOptions(string CredentialTarget,
    string Model = "deepseek-chat", string BaseUrl = "https://api.deepseek.com");

public sealed class DeepSeekChatProvider(HttpClient http, ICredentialStore credentials, DeepSeekOptions options)
    : IAiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AiChatResponse> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Messages.Count == 0) throw new ArgumentException("请求至少需要一条消息。");
        if (request.Messages.Any(m => string.IsNullOrWhiteSpace(m.Role) || string.IsNullOrWhiteSpace(m.Content)))
            throw new ArgumentException("消息角色和内容不能为空。");
        var key = credentials.ReadSecret(options.CredentialTarget);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("尚未配置 DeepSeek API Key。");
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint("/chat/completions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        message.Content = new StringContent(JsonSerializer.Serialize(new ChatCompletionRequest(
            options.Model, request.Messages.Select(m => new ChatMessage(m.Role, m.Content)).ToArray(),
            request.MaxTokens, request.Temperature), JsonOptions), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("DeepSeek API 请求失败，请检查网络、余额或模型设置。");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var payload = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content)) throw new InvalidDataException("DeepSeek API 响应为空。");
        return new AiChatResponse(content, payload?.Model ?? options.Model, "DeepSeek", DateTimeOffset.UtcNow);
    }

    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var request = new AiChatRequest([new AiMessage("user", "请只回复 OK，用于连接测试。")], 8, 0);
        var response = await CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(response.Content);
    }

    private Uri Endpoint(string path)
    {
        var root = options.BaseUrl.TrimEnd('/');
        return new Uri(root + path, UriKind.Absolute);
    }

    private sealed record ChatCompletionRequest(string Model, IReadOnlyList<ChatMessage> Messages,
        int MaxTokens, double Temperature);
    private sealed record ChatMessage(string Role, string Content);
    private sealed record ChatCompletionResponse(string? Model, IReadOnlyList<Choice>? Choices);
    private sealed record Choice(ChatMessage? Message);
}

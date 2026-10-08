using System.Net;
using System.Text.Json;
using AiKnowledgeAssistant.Core.AI;
using AiKnowledgeAssistant.Infrastructure.AI;
using AiKnowledgeAssistant.Infrastructure.Security;

var failures = new List<string>();
void Check(bool condition, string name)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {name}");
    if (!condition) failures.Add(name);
}
void Reject<T>(Func<Task> action, string name) where T : Exception
{
    var rejected = false;
    try { action().GetAwaiter().GetResult(); } catch (T) { rejected = true; }
    Check(rejected, name);
}

var credentials = new MemoryCredentialStore();
credentials.SaveSecret("aka/deepseek/test", "sk-test-secret");
var handler = new CaptureHandler();
var provider = new DeepSeekChatProvider(new HttpClient(handler), credentials,
    new DeepSeekOptions("aka/deepseek/test"));
var answer = await provider.CompleteAsync(new AiChatRequest([
    new AiMessage("system", "只回答用户问题。"),
    new AiMessage("user", "你好")
], 32, 0.1));
Check(answer.Content == "OK" && answer.Model == "deepseek-chat" && answer.Provider == "DeepSeek",
    "DeepSeek OpenAI-compatible 响应解析");
Check(handler.LastUri == "https://api.deepseek.com/chat/completions", "DeepSeek 默认接口地址");
Check(handler.LastAuthorization == "Bearer sk-test-secret", "API Key 只通过 Authorization 头发送");
Check(handler.LastBody is not null &&
    handler.LastBody.Contains("\"model\":\"deepseek-chat\"", StringComparison.Ordinal) &&
    handler.LastBody.Contains("\"role\":\"user\"", StringComparison.Ordinal) &&
    !handler.LastBody.Contains("受管理原文件", StringComparison.Ordinal),
    "请求体仅包含调用消息和模型参数");
Check(await provider.TestConnectionAsync(), "连接测试使用最小消息成功");
credentials.DeleteSecret("aka/deepseek/test");
Reject<InvalidOperationException>(() => provider.CompleteAsync(new AiChatRequest([new AiMessage("user", "hi")])),
    "缺少 API Key 时拒绝调用");
if (OperatingSystem.IsWindows())
{
    var target = "AIKnowledgeAssistant/Test/" + Guid.NewGuid().ToString("N");
    var store = new WindowsCredentialStore();
    store.SaveSecret(target, "secret-value");
    Check(store.ReadSecret(target) == "secret-value", "Windows Credential Manager 保存并读取密钥");
    store.DeleteSecret(target);
    Check(store.ReadSecret(target) is null, "Windows Credential Manager 删除密钥");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("FAILURES=" + string.Join(",", failures));
    Environment.Exit(1);
}

sealed class MemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, string> secrets = new(StringComparer.Ordinal);
    public void SaveSecret(string target, string secret) => secrets[target] = secret;
    public string? ReadSecret(string target) => secrets.GetValueOrDefault(target);
    public void DeleteSecret(string target) => secrets.Remove(target);
}

sealed class CaptureHandler : HttpMessageHandler
{
    public string? LastUri { get; private set; }
    public string? LastAuthorization { get; private set; }
    public string? LastBody { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastUri = request.RequestUri?.ToString();
        LastAuthorization = request.Headers.Authorization?.ToString();
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(LastBody ?? "{}");
        var messages = document.RootElement.GetProperty("messages");
        if (messages.EnumerateArray().Any(m => m.GetProperty("content").GetString()?.Contains("整本文档") == true))
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"model":"deepseek-chat","choices":[{"message":{"role":"assistant","content":"OK"}}]}
                """)
        };
    }
}

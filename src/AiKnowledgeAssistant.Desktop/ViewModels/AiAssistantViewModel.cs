using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using AiKnowledgeAssistant.Core.AI;
using AiKnowledgeAssistant.Core.KnowledgeBase;

namespace AiKnowledgeAssistant.Desktop.ViewModels;

public sealed class AiAssistantViewModel : INotifyPropertyChanged
{
    private readonly ICredentialStore credentials;
    private readonly string credentialTarget;
    private readonly IAiProvider provider;
    private readonly IRagService rag;
    private readonly ICitationVerifier verifier;
    private readonly Func<KnowledgeBase?> selectedKnowledgeBase;
    private string apiKeyInput = "";
    private string keyStatus;
    private string question = "";
    private string answer = "请选择知识库并输入问题。";
    private string message = "未配置 API Key 时，本地搜索仍可使用。";
    private bool isBusy;
    private bool webEnabled;
    private string publicSearchQuery = "";
    private string webKeyStatus = "";
    private const string WebCredentialTarget = "AIKnowledgeAssistant/Tavily/APIKey";
    public bool WebEnabled { get => webEnabled; set { webEnabled = value; Changed(); } }
    public string PublicSearchQuery { get => publicSearchQuery; set { publicSearchQuery = value; Changed(); } }
    public string WebKeyStatus { get => webKeyStatus; private set { webKeyStatus = value; Changed(); } }
    public ObservableCollection<WebCitation> WebCitations { get; } = new();
    public RelayCommand ClearWebKeyCommand { get; }

    public ObservableCollection<AiCitationListItem> Citations { get; } = new();
    public string ApiKeyInput { get => apiKeyInput; set { apiKeyInput = value; Changed(); } }
    public string KeyStatus { get => keyStatus; private set { keyStatus = value; Changed(); } }
    public string Question { get => question; set { question = value; Changed(); } }
    public string Answer { get => answer; private set { answer = value; Changed(); } }
    public string Message { get => message; private set { message = value; Changed(); } }
    public bool IsBusy { get => isBusy; private set { isBusy = value; Changed(); } }
    public RelayCommand SaveKeyCommand { get; }
    public RelayCommand ClearKeyCommand { get; }
    public RelayCommand TestConnectionCommand { get; }
    public RelayCommand AskCommand { get; }

    public AiAssistantViewModel(ICredentialStore credentials, string credentialTarget, IAiProvider provider,
        IRagService rag, ICitationVerifier verifier, Func<KnowledgeBase?> selectedKnowledgeBase)
    {
        this.credentials = credentials;
        this.credentialTarget = credentialTarget;
        this.provider = provider;
        this.rag = rag;
        this.verifier = verifier;
        this.selectedKnowledgeBase = selectedKnowledgeBase;
        keyStatus = HasKey() ? "已保存 DeepSeek API Key。" : "尚未保存 DeepSeek API Key。";
        WebKeyStatus = string.IsNullOrWhiteSpace(credentials.ReadSecret(WebCredentialTarget)) ? "尚未配置 Tavily 搜索 Key。" : "已保存 Tavily 搜索 Key。";
        ClearWebKeyCommand = new(() => { try { credentials.DeleteSecret(WebCredentialTarget); WebKeyStatus = "尚未配置 Tavily 搜索 Key。"; }
            catch (InvalidOperationException) { Message = "无法清除搜索凭据。"; } });
        SaveKeyCommand = new(SaveKey);
        ClearKeyCommand = new(ClearKey);
        TestConnectionCommand = new(() => _ = TestConnectionAsync());
        AskCommand = new(() => _ = AskAsync());
    }

    public void SaveWebKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) { Message = "请输入 Tavily 搜索 Key。"; return; }
        try { credentials.SaveSecret(WebCredentialTarget, key.Trim()); WebKeyStatus = "已保存 Tavily 搜索 Key。"; Message = "搜索 Key 已保存到 Windows 凭据管理器。"; }
        catch (InvalidOperationException) { Message = "无法保存搜索凭据，请检查 Windows 凭据管理器。"; }
    }

    private void SaveKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput))
        {
            Message = "请输入 DeepSeek API Key。";
            return;
        }
        credentials.SaveSecret(credentialTarget, ApiKeyInput.Trim());
        ApiKeyInput = "";
        KeyStatus = "已保存 DeepSeek API Key。";
        Message = "API Key 已保存到 Windows Credential Manager。";
    }

    private void ClearKey()
    {
        credentials.DeleteSecret(credentialTarget);
        KeyStatus = "尚未保存 DeepSeek API Key。";
        Message = "已清除 DeepSeek API Key。";
    }

    private async Task TestConnectionAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            Message = await provider.TestConnectionAsync(timeout.Token)
                ? "DeepSeek 连接测试成功。"
                : "DeepSeek 连接测试未返回有效内容。";
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            Message = FriendlyAiError(e);
        }
        finally { IsBusy = false; }
    }

    private async Task AskAsync()
    {
        if (IsBusy) return;
        var kb = selectedKnowledgeBase();
        if (kb is null && !WebEnabled)
        {
            Message = "请先选择知识库。";
            return;
        }
        if (string.IsNullOrWhiteSpace(Question))
        {
            Message = "请输入问题。";
            return;
        }
        if (WebEnabled && string.IsNullOrWhiteSpace(PublicSearchQuery)) { Message = "请输入公开搜索词。"; return; }
        if (!HasKey()) { Message = "尚未配置 DeepSeek API Key。"; return; }
        var requestQuestion = Question.Trim();
        var publicQuery = WebEnabled ? PublicSearchQuery.Trim() : null;
        Guid[] selectedIds = kb is null ? [] : [kb.Id];
        IsBusy = true;
        Citations.Clear();
        WebCitations.Clear();
        Message = publicQuery is null ? "正在检索知识库并生成回答…" : "正在检索知识库与公网并综合回答…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await Task.Run(() => publicQuery is null
                ? rag.AnswerAsync(requestQuestion, selectedIds, timeout.Token)
                : rag.AnswerWithWebAsync(requestQuestion, selectedIds, publicQuery, timeout.Token));
            Answer = result.Answer;
            var verification = verifier.Verify(result);
            foreach (var item in verification.VerifiedCitations)
                Citations.Add(new AiCitationListItem(item.SourceId, item.Source.FileName,
                    item.Source.Position, item.Source.Text, item.Source.ManagedFilePath, item.Source.CurrentPage, item.Source.PageCount));
            foreach (var item in verification.WebCitations) WebCitations.Add(item);
            Message = !result.UsedAi ? result.Answer : verification.IsValid ? "回答已生成，来源编号已匹配；请核对引用内容。" :
                "回答已生成，但引用核验未通过：" + string.Join(", ", verification.MissingSourceIds);
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or OperationCanceledException or InvalidDataException or ArgumentException or WebSearchException)
        {
            Answer = "";
            Message = FriendlyAiError(e);
        }
        finally { IsBusy = false; }
    }

    private bool HasKey() => !string.IsNullOrWhiteSpace(credentials.ReadSecret(credentialTarget));

    private static string FriendlyAiError(Exception e) => e switch
    {
        WebSearchException => e.Message,
        OperationCanceledException => "问答请求超时，请稍后重试。",
        HttpRequestException => "DeepSeek 网络连接失败，请检查网络。",
        InvalidOperationException when e.Message.Contains("API Key", StringComparison.OrdinalIgnoreCase) =>
            "尚未配置 DeepSeek API Key。",
        InvalidOperationException => "DeepSeek API 请求失败，请检查 Key、余额、网络或模型设置。",
        InvalidDataException => "DeepSeek 响应为空或格式异常。",
        ArgumentException => "问题或请求参数无效。",
        _ => "AI 问答失败。"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record AiCitationListItem(string SourceId, string FileName, string Position, string Text,
    string FilePath = "", int? PageNumber = null, int? PageCount = null)
{
    public bool IsPdf => PageNumber.HasValue && PageCount.HasValue;
    public string Title => $"{SourceId} · {FileName}";
    public string Preview => Text.Length > 500 ? Text[..500] + "..." : Text;
}

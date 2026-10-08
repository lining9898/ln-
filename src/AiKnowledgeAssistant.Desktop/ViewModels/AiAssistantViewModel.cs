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
        SaveKeyCommand = new(SaveKey);
        ClearKeyCommand = new(ClearKey);
        TestConnectionCommand = new(() => _ = TestConnectionAsync());
        AskCommand = new(() => _ = AskAsync());
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
        if (kb is null)
        {
            Message = "请先选择知识库。";
            return;
        }
        if (string.IsNullOrWhiteSpace(Question))
        {
            Message = "请输入问题。";
            return;
        }
        IsBusy = true;
        Citations.Clear();
        Message = "正在检索知识库并生成回答…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await Task.Run(() => rag.AnswerAsync(Question.Trim(), [kb.Id], timeout.Token));
            Answer = result.Answer;
            var verification = verifier.Verify(result);
            foreach (var item in verification.VerifiedCitations)
                Citations.Add(new AiCitationListItem(item.SourceId, item.Source.FileName,
                    item.Source.Position, item.Source.Text));
            Message = verification.IsValid ? "回答已生成，并完成引用核验。" :
                "回答已生成，但引用核验未通过：" + string.Join(", ", verification.MissingSourceIds);
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException or InvalidDataException or ArgumentException)
        {
            Answer = "";
            Message = FriendlyAiError(e);
        }
        finally { IsBusy = false; }
    }

    private bool HasKey() => !string.IsNullOrWhiteSpace(credentials.ReadSecret(credentialTarget));

    private static string FriendlyAiError(Exception e) => e switch
    {
        TaskCanceledException => "DeepSeek 请求超时，请稍后重试。",
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

public sealed record AiCitationListItem(string SourceId, string FileName, string Position, string Text)
{
    public string Title => $"{SourceId} · {FileName}";
    public string Preview => Text.Length > 500 ? Text[..500] + "..." : Text;
}

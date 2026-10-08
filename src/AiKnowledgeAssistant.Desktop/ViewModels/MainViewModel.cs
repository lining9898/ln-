using System.ComponentModel;
using System.Runtime.CompilerServices;
using AiKnowledgeAssistant.Core.Storage;

namespace AiKnowledgeAssistant.Desktop.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public IReadOnlyList<string> NavigationItems { get; } =
        new[] { "AI问答", "知识库", "文档搜索", "文件管理", "设置" };
    private string selectedPage = "AI问答";
    public string SelectedPage
    {
        get => selectedPage;
        set
        {
            if (!NavigationItems.Contains(value) || selectedPage == value) return;
            selectedPage = value;
            OnPropertyChanged();
        }
    }
    public KnowledgeBasesViewModel? KnowledgeBases { get; }
    public DocumentsViewModel? Documents { get; }
    public DocumentSearchViewModel? Search { get; }
    public AiAssistantViewModel? Ai { get; }
    public string DataRoot { get; }
    public MainViewModel(IUserDataPaths paths, KnowledgeBasesViewModel? knowledgeBases = null,
        DocumentsViewModel? documents = null, DocumentSearchViewModel? search = null,
        AiAssistantViewModel? ai = null)
    {
        DataRoot = paths.Root;
        KnowledgeBases = knowledgeBases;
        Documents = documents;
        Search = search;
        Ai = ai;
        if (knowledgeBases is not null && search is not null)
            knowledgeBases.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(KnowledgeBasesViewModel.Selected)) search.ClearResults();
            };
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

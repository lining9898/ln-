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
    public string DataRoot { get; }
    public MainViewModel(IUserDataPaths paths) => DataRoot = paths.Root;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

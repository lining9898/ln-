using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using AiKnowledgeAssistant.Core.KnowledgeBase;

namespace AiKnowledgeAssistant.Desktop.ViewModels;

public sealed class KnowledgeBasesViewModel : INotifyPropertyChanged
{
    private readonly IKnowledgeBaseStore store;
    private readonly IKnowledgeBaseDialogs dialogs;
    public ObservableCollection<KnowledgeBase> Items { get; } = new();
    public DocumentsViewModel? Documents { get; set; }
    public RelayCommand CreateCommand { get; }
    public RelayCommand RenameCommand { get; }
    public RelayCommand DeleteCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public bool HasItems => Items.Count > 0;
    public string Summary => $"共 {Items.Count} 个知识库";
    private KnowledgeBase? selected;
    public KnowledgeBase? Selected
    {
        get => selected;
        set
        {
            if (selected == value) return;
            selected = value;
            OnPropertyChanged();
            Documents?.SelectKnowledgeBase(value);
            RenameCommand.NotifyCanExecuteChanged();
            DeleteCommand.NotifyCanExecuteChanged();
        }
    }

    public KnowledgeBasesViewModel(IKnowledgeBaseStore store, IKnowledgeBaseDialogs dialogs)
    {
        this.store = store;
        this.dialogs = dialogs;
        CreateCommand = new(() => dialogs.EditName("新建知识库", "", name =>
        {
            var item = store.Create(name);
            Reload(item.Id);
        }));
        RenameCommand = new(() =>
        {
            var item = Selected!;
            dialogs.EditName("重命名知识库", item.Name, name =>
            {
                store.Rename(item.Id, name);
                Reload(item.Id);
            });
        }, () => Selected is not null);
        DeleteCommand = new(DeleteSelected, () => Selected is not null);
        RefreshCommand = new(Refresh);
    }

    private void DeleteSelected()
    {
        var item = Selected!;
        if (!dialogs.ConfirmDelete(item)) return;
        try
        {
            store.Delete(item.Id);
            Reload();
        }
        catch (Exception e) when (IsExpectedError(e)) { dialogs.ShowError(ErrorMessage(e)); }
    }

    public void Refresh()
    {
        try { Reload(Selected?.Id); }
        catch (Exception e) when (IsExpectedError(e)) { dialogs.ShowError(ErrorMessage(e)); }
    }

    private void Reload(Guid? selectedId = null)
    {
        var items = store.List(); // Read first: failures retain visible list.
        Selected = null;
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        Selected = Items.FirstOrDefault(k => k.Id == selectedId);
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Summary));
    }

    public static bool IsExpectedError(Exception e) =>
        e is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException;
    public static string ErrorMessage(Exception e) => e switch
    {
        InvalidDataException => "知识库数据损坏或版本不受支持，已停止操作。请保留原文件。",
        IOException or UnauthorizedAccessException => "无法读写知识库数据。请检查目录权限、磁盘空间，或关闭其他正在操作知识库的软件窗口后重试。",
        _ => e.Message
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

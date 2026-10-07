using System.Windows;
using AiKnowledgeAssistant.Core.KnowledgeBase;
using AiKnowledgeAssistant.Desktop.ViewModels;

namespace AiKnowledgeAssistant.Desktop.UI;

public sealed class KnowledgeBaseDialogs(Window owner) : IKnowledgeBaseDialogs
{
    public void EditName(string title, string initialName, Action<string> save) =>
        new KnowledgeBaseNameDialog(title, initialName, save) { Owner = owner }.ShowDialog();

    public bool ConfirmDelete(KnowledgeBase knowledgeBase) => MessageBox.Show(owner,
        $"确定删除知识库“{knowledgeBase.Name}”吗？\n删除后无法撤销，其他知识库不会受到影响。",
        "确认删除知识库", MessageBoxButton.YesNo, MessageBoxImage.Warning,
        MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowError(string message) => MessageBox.Show(owner, message,
        "知识库操作未完成", MessageBoxButton.OK, MessageBoxImage.Warning);
}

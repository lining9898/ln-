using AiKnowledgeAssistant.Core.KnowledgeBase;

namespace AiKnowledgeAssistant.Desktop.ViewModels;

public interface IKnowledgeBaseDialogs
{
    void EditName(string title, string initialName, Action<string> save);
    bool ConfirmDelete(KnowledgeBase knowledgeBase);
    void ShowError(string message);
}

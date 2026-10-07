namespace AiKnowledgeAssistant.Core.KnowledgeBase;

public interface IKnowledgeBaseStore
{
    IReadOnlyList<KnowledgeBase> List();
    KnowledgeBase Create(string name);
    KnowledgeBase Rename(Guid id, string name);
    void Delete(Guid id);
}

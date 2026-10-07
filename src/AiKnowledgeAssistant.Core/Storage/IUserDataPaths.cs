namespace AiKnowledgeAssistant.Core.Storage;

public interface IUserDataPaths
{
    string Root { get; }
    string Databases { get; }
    string Documents { get; }
    string Indexes { get; }
    string Cache { get; }
    string Config { get; }
    void EnsureDirectories();
}

using AiKnowledgeAssistant.Core.Storage;

namespace AiKnowledgeAssistant.Infrastructure.Storage;

public sealed class UserDataPaths : IUserDataPaths
{
    public string Root { get; }
    public string Databases => Path.Combine(Root, "databases");
    public string Documents => Path.Combine(Root, "documents");
    public string Indexes => Path.Combine(Root, "indexes");
    public string Cache => Path.Combine(Root, "cache");
    public string Config => Path.Combine(Root, "config");

    // Explicit base path permits isolated tests without touching real user data.
    public UserDataPaths(string localApplicationData)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationData);
        if (!Path.IsPathFullyQualified(localApplicationData))
            throw new ArgumentException("用户数据基础路径必须是绝对路径。", nameof(localApplicationData));
        Root = Path.Combine(localApplicationData, "AIKnowledgeAssistant", "data");
    }

    public static UserDataPaths ForCurrentUser() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public void EnsureDirectories()
    {
        foreach (var directory in new[] { Root, Databases, Documents, Indexes, Cache, Config })
            Directory.CreateDirectory(directory);
    }
}

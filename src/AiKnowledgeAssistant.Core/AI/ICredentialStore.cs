namespace AiKnowledgeAssistant.Core.AI;

public interface ICredentialStore
{
    void SaveSecret(string target, string secret);
    string? ReadSecret(string target);
    void DeleteSecret(string target);
}

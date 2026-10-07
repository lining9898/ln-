namespace AiKnowledgeAssistant.Infrastructure.Documents;

public interface IFileTransfer
{
    (long Size, string Hash) CopyAndHash(string sourcePath, string stagingPath);
}

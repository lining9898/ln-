using System.Security.Cryptography;

namespace AiKnowledgeAssistant.Infrastructure.Documents;

public sealed class LocalFileTransfer : IFileTransfer
{
    public (long Size, string Hash) CopyAndHash(string sourcePath, string stagingPath)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan);
        using var target = new FileStream(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 1024 * 1024, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long size = 0;
        int read;
        while ((read = source.Read(buffer)) != 0)
        {
            target.Write(buffer, 0, read);
            hash.AppendData(buffer, 0, read);
            size += read;
        }
        target.Flush(flushToDisk: true);
        return (size, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }
}

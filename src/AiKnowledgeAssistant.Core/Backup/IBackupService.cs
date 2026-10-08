namespace AiKnowledgeAssistant.Core.Backup;

public sealed record BackupFileEntry(string Path, string Sha256, long Size);

public sealed record BackupManifest(int Version, DateTimeOffset CreatedAt,
    IReadOnlyList<BackupFileEntry> Files);

public interface IBackupService
{
    BackupManifest CreateBackup(string destinationZipPath);
    BackupManifest RestoreBackup(string sourceZipPath);
}

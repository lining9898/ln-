using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AiKnowledgeAssistant.Core.Backup;
using AiKnowledgeAssistant.Core.Storage;
using AiKnowledgeAssistant.Infrastructure.Database;

namespace AiKnowledgeAssistant.Infrastructure.Backup;

public sealed class LocalBackupService(SqliteDatabase database, IUserDataPaths paths) : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public BackupManifest CreateBackup(string destinationZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationZipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationZipPath))!);
        var temp = Path.Combine(Path.GetTempPath(), "aka-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var databaseSnapshot = Path.Combine(temp, "knowledge.db");
            using (var db = database.Open())
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = "VACUUM INTO $path;";
                cmd.Parameters.AddWithValue("$path", databaseSnapshot);
                cmd.ExecuteNonQuery();
            }
            var files = new List<(string ArchivePath, string FullPath)>();
            files.Add(("databases/knowledge.db", databaseSnapshot));
            if (Directory.Exists(paths.Documents))
            {
                foreach (var file in Directory.EnumerateFiles(paths.Documents, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(paths.Documents, file).Replace('\\', '/');
                    files.Add(("documents/" + relative, file));
                }
            }
            var manifest = new BackupManifest(1, DateTimeOffset.UtcNow,
                files.Select(f => new BackupFileEntry(f.ArchivePath, Sha256(f.FullPath), new FileInfo(f.FullPath).Length)).ToArray());
            var manifestPath = Path.Combine(temp, "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
            if (File.Exists(destinationZipPath)) File.Delete(destinationZipPath);
            using var archive = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(manifestPath, "manifest.json", CompressionLevel.Optimal);
            foreach (var file in files)
                archive.CreateEntryFromFile(file.FullPath, file.ArchivePath, CompressionLevel.Optimal);
            return manifest;
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    public BackupManifest RestoreBackup(string sourceZipPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceZipPath);
        if (!File.Exists(sourceZipPath)) throw new FileNotFoundException("备份文件不存在。", sourceZipPath);
        Directory.CreateDirectory(paths.Databases);
        Directory.CreateDirectory(paths.Documents);
        if (Directory.EnumerateFileSystemEntries(paths.Databases).Any() ||
            Directory.EnumerateFileSystemEntries(paths.Documents).Any())
            throw new InvalidOperationException("恢复目标已有数据，默认不会覆盖。");
        var temp = Path.Combine(Path.GetTempPath(), "aka-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using (var archive = ZipFile.OpenRead(sourceZipPath))
            {
                foreach (var entry in archive.Entries) ValidateEntryName(entry.FullName);
                var manifestEntry = archive.GetEntry("manifest.json") ??
                    throw new InvalidDataException("备份缺少 manifest。");
                BackupManifest manifest;
                using (var stream = manifestEntry.Open())
                    manifest = JsonSerializer.Deserialize<BackupManifest>(stream) ??
                        throw new InvalidDataException("manifest 无法读取。");
                if (manifest.Version != 1) throw new InvalidDataException("备份版本不受支持。");
                foreach (var file in manifest.Files)
                {
                    ValidateEntryName(file.Path);
                    var entry = archive.GetEntry(file.Path) ??
                        throw new InvalidDataException("备份缺少文件：" + file.Path);
                    var target = Path.Combine(temp, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target);
                    if (new FileInfo(target).Length != file.Size || !Sha256(target).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("备份文件校验失败：" + file.Path);
                }
                var dbSource = Path.Combine(temp, "databases", "knowledge.db");
                File.Copy(dbSource, database.FilePath);
                var docsSource = Path.Combine(temp, "documents");
                if (Directory.Exists(docsSource))
                    CopyDirectory(docsSource, paths.Documents);
                return manifest;
            }
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static void ValidateEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.StartsWith('/') ||
            name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
            throw new InvalidDataException("备份包含非法路径。");
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: false);
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

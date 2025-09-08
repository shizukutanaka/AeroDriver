using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// ファイル操作ヘルパー
    /// </summary>
    public static class FileHelper
    {
        /// <summary>
        /// 安全にファイルを削除
        /// </summary>
        public static bool SafeDelete(string filePath, ILogger? logger = null)
        {
            try
            {
                if (!File.Exists(filePath))
                    return true;

                // Read-onlyチェック
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.IsReadOnly)
                {
                    fileInfo.IsReadOnly = false;
                }

                File.Delete(filePath);
                logger?.LogTrace("File deleted: {FilePath}", filePath);
                return true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to delete file: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// ファイルサイズを人間が読みやすい形式で取得
        /// </summary>
        public static string GetHumanReadableSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        /// <summary>
        /// ディスクの空き容量をチェック
        /// </summary>
        public static bool HasEnoughSpace(string path, long requiredBytes)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(path) ?? "C:\\");
                return drive.AvailableFreeSpace >= requiredBytes;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ファイルのハッシュを計算
        /// </summary>
        public static async Task<string> CalculateHashAsync(string filePath, HashAlgorithm? algorithm = null)
        {
            algorithm ??= SHA256.Create();
            
            try
            {
                using var stream = File.OpenRead(filePath);
                var hash = await Task.Run(() => algorithm.ComputeHash(stream));
                return Convert.ToHexString(hash).ToLowerInvariant();
            }
            finally
            {
                algorithm.Dispose();
            }
        }

        /// <summary>
        /// 安全にファイルをコピー
        /// </summary>
        public static async Task<bool> SafeCopyAsync(string sourcePath, string destinationPath, bool overwrite = false, ILogger? logger = null)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    logger?.LogWarning("Source file does not exist: {SourcePath}", sourcePath);
                    return false;
                }

                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                // Check for enough disk space
                var fileInfo = new FileInfo(sourcePath);
                if (!HasEnoughSpace(destinationPath, fileInfo.Length))
                {
                    logger?.LogWarning("Insufficient disk space for copy operation: {Size} bytes required", fileInfo.Length);
                    return false;
                }

                using var source = File.OpenRead(sourcePath);
                using var destination = File.Create(destinationPath);
                
                await source.CopyToAsync(destination);
                
                logger?.LogTrace("File copied: {SourcePath} -> {DestinationPath}", sourcePath, destinationPath);
                return true;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to copy file: {SourcePath} -> {DestinationPath}", sourcePath, destinationPath);
                return false;
            }
        }

        /// <summary>
        /// 一時ファイルの管理
        /// </summary>
        public static string CreateTempFile(string? extension = null)
        {
            var tempPath = Path.GetTempPath();
            var fileName = Path.GetRandomFileName();
            
            if (!string.IsNullOrEmpty(extension))
            {
                fileName = Path.ChangeExtension(fileName, extension);
            }
            
            return Path.Combine(tempPath, "AeroDriver", fileName);
        }

        /// <summary>
        /// 古いファイルをクリーンアップ
        /// </summary>
        public static int CleanupOldFiles(string directory, TimeSpan maxAge, string searchPattern = "*", ILogger? logger = null)
        {
            if (!Directory.Exists(directory))
                return 0;

            var cutoffTime = DateTime.Now - maxAge;
            var deletedCount = 0;

            try
            {
                var files = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
                    .Where(file => File.GetLastWriteTime(file) < cutoffTime);

                foreach (var file in files)
                {
                    if (SafeDelete(file, logger))
                    {
                        deletedCount++;
                    }
                }

                logger?.LogInformation("Cleaned up {Count} old files from {Directory}", deletedCount, directory);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during cleanup in directory: {Directory}", directory);
            }

            return deletedCount;
        }

        /// <summary>
        /// ファイルが使用中かどうかチェック
        /// </summary>
        public static bool IsFileLocked(string filePath)
        {
            try
            {
                using var stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// バックアップファイルを作成
        /// </summary>
        public static async Task<string?> CreateBackupAsync(string originalPath, string? backupDirectory = null, ILogger? logger = null)
        {
            try
            {
                if (!File.Exists(originalPath))
                    return null;

                var fileName = Path.GetFileName(originalPath);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupFileName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}";
                
                backupDirectory ??= Path.Combine(Path.GetDirectoryName(originalPath) ?? "", "Backups");
                Directory.CreateDirectory(backupDirectory);
                
                var backupPath = Path.Combine(backupDirectory, backupFileName);
                
                if (await SafeCopyAsync(originalPath, backupPath, false, logger))
                {
                    logger?.LogInformation("Backup created: {BackupPath}", backupPath);
                    return backupPath;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to create backup for: {OriginalPath}", originalPath);
                return null;
            }
        }
    }
}
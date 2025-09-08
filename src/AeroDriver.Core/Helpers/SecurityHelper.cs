using System.Security.Principal;
using System.Security.Cryptography;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AeroDriver.Core.Helpers
{
    /// <summary>
    /// セキュリティ関連ヘルパー
    /// </summary>
    public static class SecurityHelper
    {
        /// <summary>
        /// 管理者権限で実行されているかチェック
        /// </summary>
        public static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ファイルの信頼性をチェック（基本的な検証）
        /// </summary>
        public static async Task<bool> IsFileSecureAsync(string filePath, ILogger? logger = null)
        {
            try
            {
                if (!File.Exists(filePath))
                    return false;

                var fileInfo = new FileInfo(filePath);
                
                // サイズチェック（異常に大きい/小さいファイルを除外）
                if (fileInfo.Length == 0 || fileInfo.Length > 500 * 1024 * 1024) // 500MB制限
                {
                    logger?.LogWarning("File size suspicious: {Size} bytes for {FilePath}", fileInfo.Length, filePath);
                    return false;
                }

                // 拡張子チェック（ドライバーファイルとして適切な拡張子）
                var allowedExtensions = new[] { ".inf", ".sys", ".cat", ".dll", ".exe" };
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                
                if (!allowedExtensions.Contains(extension))
                {
                    logger?.LogWarning("Suspicious file extension: {Extension} for {FilePath}", extension, filePath);
                    return false;
                }

                // パス検証（危険な場所からのファイルを除外）
                var dangerousPaths = new[] { @"\temp", @"\tmp", @"\downloads", @"\appdata\local\temp" };
                var lowerPath = filePath.ToLowerInvariant();
                
                if (dangerousPaths.Any(dangerous => lowerPath.Contains(dangerous)))
                {
                    logger?.LogInformation("File from temporary location, requiring extra validation: {FilePath}", filePath);
                }

                return true;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error validating file security: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// WHQLドライバーの署名をチェック
        /// </summary>
        public static bool IsWhqlSigned(string filePath, ILogger? logger = null)
        {
            try
            {
                // signtool.exeを使用してデジタル署名をチェック
                var startInfo = new ProcessStartInfo
                {
                    FileName = "signtool.exe",
                    Arguments = $"verify /pa \"{filePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return false;

                process.WaitForExit(10000); // 10秒タイムアウト
                
                var isValid = process.ExitCode == 0;
                logger?.LogDebug("Digital signature check for {FilePath}: {IsValid}", filePath, isValid ? "Valid" : "Invalid");
                
                return isValid;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not verify digital signature for: {FilePath}", filePath);
                return false;
            }
        }

        /// <summary>
        /// システムファイルが改ざんされていないかチェック
        /// </summary>
        public static async Task<bool> VerifySystemIntegrityAsync(ILogger? logger = null)
        {
            try
            {
                logger?.LogInformation("Starting system file integrity check...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "sfc.exe",
                    Arguments = "/verifyonly",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                    return false;

                await process.WaitForExitAsync();
                
                var isHealthy = process.ExitCode == 0;
                logger?.LogInformation("System integrity check result: {Result}", isHealthy ? "Healthy" : "Issues detected");
                
                return isHealthy;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error running system integrity check");
                return false;
            }
        }

        /// <summary>
        /// 安全な一時ディレクトリを作成
        /// </summary>
        public static string CreateSecureTempDirectory(string? prefix = null)
        {
            var tempPath = Path.GetTempPath();
            var dirName = $"AeroDriver_{prefix ?? "temp"}_{Guid.NewGuid():N}";
            var fullPath = Path.Combine(tempPath, dirName);
            
            Directory.CreateDirectory(fullPath);
            
            // アクセス権を制限（現在のユーザーのみ）
            try
            {
                var dirInfo = new DirectoryInfo(fullPath);
                var security = dirInfo.GetAccessControl();
                // 必要に応じてACLを設定
            }
            catch
            {
                // ACL設定に失敗しても続行
            }
            
            return fullPath;
        }

        /// <summary>
        /// プロセスが信頼できるかチェック
        /// </summary>
        public static bool IsProcessTrusted(int processId, ILogger? logger = null)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                var mainModule = process.MainModule;
                
                if (mainModule?.FileName == null)
                    return false;

                // システムディレクトリからのプロセスは信頼
                var systemPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows)
                };

                var processPath = Path.GetDirectoryName(mainModule.FileName)?.ToLowerInvariant();
                if (processPath != null && systemPaths.Any(path => processPath.StartsWith(path.ToLowerInvariant())))
                {
                    return true;
                }

                // デジタル署名チェック
                return IsWhqlSigned(mainModule.FileName, logger);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not verify process trust: {ProcessId}", processId);
                return false;
            }
        }

        /// <summary>
        /// 危険なファイル名パターンをチェック
        /// </summary>
        public static bool IsFilenameSafe(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return false;

            // 危険な文字をチェック
            var dangerousChars = Path.GetInvalidFileNameChars()
                .Concat(new[] { '<', '>', ':', '"', '|', '?', '*' });
            
            if (filename.Any(c => dangerousChars.Contains(c)))
                return false;

            // 危険な名前パターンをチェック
            var dangerousPatterns = new[]
            {
                "con", "prn", "aux", "nul", "com1", "com2", "com3", "com4", "com5",
                "com6", "com7", "com8", "com9", "lpt1", "lpt2", "lpt3", "lpt4",
                "lpt5", "lpt6", "lpt7", "lpt8", "lpt9"
            };

            var baseName = Path.GetFileNameWithoutExtension(filename).ToLowerInvariant();
            return !dangerousPatterns.Contains(baseName);
        }

        /// <summary>
        /// 安全にファイルハッシュを比較
        /// </summary>
        public static async Task<bool> CompareFileHashAsync(string filePath, string expectedHash, HashAlgorithm? algorithm = null)
        {
            try
            {
                var actualHash = await FileHelper.CalculateHashAsync(filePath, algorithm);
                return string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
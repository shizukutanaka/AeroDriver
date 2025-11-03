using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AeroDriver.Core.Security;

/// <summary>
/// エンタープライズグレードの暗号化ユーティリティ
/// AES-256標準を採用し、量子耐性暗号への移行を考慮した設計
/// </summary>
public static class EncryptionManager
{
    private const int AesKeySize = 256;
    private const int AesBlockSize = 128;
    private const int Pbkdf2Iterations = 100000;
    private const int SaltSize = 32;
    private const int IvSize = 16;

    private static readonly SimpleLogger Logger = new();

    /// <summary>
    /// 文字列をAES-256で暗号化します
    /// </summary>
    public static async Task<string> EncryptStringAsync(string plainText, string password)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be null or empty", nameof(password));

        try
        {
            using var aes = Aes.Create();
            aes.KeySize = AesKeySize;
            aes.BlockSize = AesBlockSize;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // パスワードからキーとIVを派生
            using var deriveBytes = new Rfc2898DeriveBytes(password, SaltSize, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            var salt = deriveBytes.Salt;
            var key = deriveBytes.GetBytes(aes.KeySize / 8);
            var iv = deriveBytes.GetBytes(aes.BlockSize / 8);

            using var encryptor = aes.CreateEncryptor(key, iv);
            await using var memoryStream = new MemoryStream();
            await using var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write);

            // SaltとIVを先頭に書き込み
            await memoryStream.WriteAsync(salt, 0, salt.Length);
            await memoryStream.WriteAsync(iv, 0, iv.Length);

            // プレーンテキストを暗号化
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            await cryptoStream.WriteAsync(plainBytes, 0, plainBytes.Length);
            await cryptoStream.FlushFinalBlockAsync();

            var cipherBytes = memoryStream.ToArray();
            return Convert.ToBase64String(cipherBytes);
        }
        catch (Exception ex)
        {
            await Logger.LogSecurityEventAsync("EncryptionError", $"Failed to encrypt string: {ex.Message}");
            throw new SecurityException("Encryption failed", ex);
        }
    }

    /// <summary>
    /// AES-256で暗号化された文字列を復号化します
    /// </summary>
    public static async Task<string> DecryptStringAsync(string cipherText, string password)
    {
        if (string.IsNullOrEmpty(cipherText))
            return string.Empty;

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password cannot be null or empty", nameof(password));

        try
        {
            var cipherBytes = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.KeySize = AesKeySize;
            aes.BlockSize = AesBlockSize;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            // SaltとIVを抽出
            var salt = cipherBytes.Take(SaltSize).ToArray();
            var iv = cipherBytes.Skip(SaltSize).Take(IvSize).ToArray();
            var encryptedData = cipherBytes.Skip(SaltSize + IvSize).ToArray();

            // パスワードからキーを派生
            using var deriveBytes = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            var key = deriveBytes.GetBytes(aes.KeySize / 8);

            using var decryptor = aes.CreateDecryptor(key, iv);
            await using var memoryStream = new MemoryStream(encryptedData);
            await using var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read);

            using var outputStream = new MemoryStream();
            await cryptoStream.CopyToAsync(outputStream);
            var decryptedBytes = outputStream.ToArray();

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception ex)
        {
            await Logger.LogSecurityEventAsync("DecryptionError", $"Failed to decrypt string: {ex.Message}");
            throw new SecurityException("Decryption failed", ex);
        }
    }

    /// <summary>
    /// ファイルの内容を暗号化して新しいファイルに保存します
    /// </summary>
    public static async Task EncryptFileAsync(string sourceFilePath, string destinationFilePath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath, nameof(sourceFilePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath, nameof(destinationFilePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(password, nameof(password));

        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Source file not found", sourceFilePath);

        if (!SecurityUtilities.IsValidFilePath(sourceFilePath))
            throw new UnauthorizedAccessException("Invalid source file path");

        if (!SecurityUtilities.IsValidFilePath(destinationFilePath))
            throw new UnauthorizedAccessException("Invalid destination file path");

        try
        {
            var plainText = await File.ReadAllTextAsync(sourceFilePath);
            var encryptedText = await EncryptStringAsync(plainText, password);

            // ディレクトリが存在しない場合は作成
            var destDir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            await File.WriteAllTextAsync(destinationFilePath, encryptedText);

            await Logger.LogSecurityEventAsync("FileEncrypted",
                $"File encrypted successfully: {Path.GetFileName(sourceFilePath)}");
        }
        catch (Exception ex)
        {
            await Logger.LogSecurityEventAsync("FileEncryptionError",
                $"Failed to encrypt file {sourceFilePath}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 暗号化されたファイルの内容を復号化します
    /// </summary>
    public static async Task<string> DecryptFileAsync(string encryptedFilePath, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedFilePath, nameof(encryptedFilePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(password, nameof(password));

        if (!File.Exists(encryptedFilePath))
            throw new FileNotFoundException("Encrypted file not found", encryptedFilePath);

        if (!SecurityUtilities.IsValidFilePath(encryptedFilePath))
            throw new UnauthorizedAccessException("Invalid encrypted file path");

        try
        {
            var encryptedText = await File.ReadAllTextAsync(encryptedFilePath);
            return await DecryptStringAsync(encryptedText, password);
        }
        catch (Exception ex)
        {
            await Logger.LogSecurityEventAsync("FileDecryptionError",
                $"Failed to decrypt file {encryptedFilePath}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// データのハッシュ値を計算します（SHA-256）
    /// </summary>
    public static async Task<string> ComputeHashAsync(string data)
    {
        if (string.IsNullOrEmpty(data))
            return string.Empty;

        using var sha256 = SHA256.Create();
        var dataBytes = Encoding.UTF8.GetBytes(data);
        var hashBytes = await Task.Run(() => sha256.ComputeHash(dataBytes));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// データのハッシュ値を計算します（SHA-256）
    /// </summary>
    public static async Task<string> ComputeHashAsync(byte[] data)
    {
        if (data == null || data.Length == 0)
            return string.Empty;

        using var sha256 = SHA256.Create();
        var hashBytes = await Task.Run(() => sha256.ComputeHash(data));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// ファイルのハッシュ値を計算します（SHA-256）
    /// </summary>
    public static async Task<string> ComputeFileHashAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        if (!SecurityUtilities.IsValidFilePath(filePath))
            throw new UnauthorizedAccessException("Invalid file path");

        try
        {
            using var sha256 = SHA256.Create();
            await using var stream = File.OpenRead(filePath);
            var hashBytes = await Task.Run(() => sha256.ComputeHash(stream));
            return Convert.ToBase64String(hashBytes);
        }
        catch (Exception ex)
        {
            await Logger.LogSecurityEventAsync("HashComputationError",
                $"Failed to compute hash for file {filePath}: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 機密データを検出して保護します
    /// </summary>
    public static async Task<SensitiveDataProtectionResult> DetectAndProtectSensitiveDataAsync(string content)
    {
        if (string.IsNullOrEmpty(content))
            return new SensitiveDataProtectionResult { IsProtected = false };

        var result = new SensitiveDataProtectionResult
        {
            OriginalContent = content,
            DetectedSensitiveTypes = new List<string>(),
            ProtectionApplied = new List<string>(),
            IsProtected = false
        };

        // 機密データパターンの検出と保護
        var protectedContent = content;

        // クレジットカード番号の検出と保護
        if (Regex.IsMatch(content, @"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b"))
        {
            protectedContent = Regex.Replace(protectedContent, @"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", "****-****-****-****");
            result.DetectedSensitiveTypes.Add("CreditCard");
            result.ProtectionApplied.Add("CreditCard");
        }

        // SSNの検出と保護
        if (Regex.IsMatch(content, @"\b\d{3}-\d{2}-\d{4}\b"))
        {
            protectedContent = Regex.Replace(protectedContent, @"\b\d{3}-\d{2}-\d{4}\b", "***-**-****");
            result.DetectedSensitiveTypes.Add("SocialSecurityNumber");
            result.ProtectionApplied.Add("SSN");
        }

        // メールアドレスの検出と保護
        if (Regex.IsMatch(content, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}"))
        {
            protectedContent = Regex.Replace(protectedContent, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", "***@***.***");
            result.DetectedSensitiveTypes.Add("Email");
            result.ProtectionApplied.Add("Email");
        }

        // APIキー/トークンの検出と保護（長い英数字文字列）
        if (Regex.IsMatch(content, @"\b[A-Za-z0-9]{32,}\b"))
        {
            protectedContent = Regex.Replace(protectedContent, @"\b[A-Za-z0-9]{32,}\b", "***API_KEY***");
            result.DetectedSensitiveTypes.Add("ApiKey");
            result.ProtectionApplied.Add("ApiKey");
        }

        // パスワードパターンの検出と保護
        if (Regex.IsMatch(content, @"(password|pwd|passwd|secret|token|key)\s*[:=]\s*\S+", RegexOptions.IgnoreCase))
        {
            protectedContent = Regex.Replace(protectedContent, @"(password|pwd|passwd|secret|token|key)\s*[:=]\s*\S+", "$1=***", RegexOptions.IgnoreCase);
            result.DetectedSensitiveTypes.Add("Password");
            result.ProtectionApplied.Add("Password");
        }

        // JWTトークンの検出と保護
        if (Regex.IsMatch(content, @"[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9\-_.]+"))
        {
            protectedContent = Regex.Replace(protectedContent, @"[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9\-_.]+", "***JWT_TOKEN***");
            result.DetectedSensitiveTypes.Add("JwtToken");
            result.ProtectionApplied.Add("JwtToken");
        }

        result.ProtectedContent = protectedContent;
        result.IsProtected = result.ProtectionApplied.Any();

        if (result.IsProtected)
        {
            await Logger.LogSecurityEventAsync("SensitiveDataProtected",
                $"Sensitive data detected and protected: {string.Join(", ", result.DetectedSensitiveTypes)}");
        }

        return result;
    }

    /// <summary>
    /// データの分類を実行します（機密性レベル判定）
    /// </summary>
    public static async Task<DataClassificationResult> ClassifyDataAsync(string content)
    {
        var result = new DataClassificationResult
        {
            ContentLength = content?.Length ?? 0,
            ClassificationTime = DateTime.UtcNow,
            SensitivityScore = 0,
            RiskLevel = RiskLevel.Low,
            DetectedPatterns = new List<string>(),
            Recommendations = new List<string>()
        };

        if (string.IsNullOrEmpty(content))
        {
            result.RiskLevel = RiskLevel.None;
            return result;
        }

        // パターン別スコアリング
        var patterns = new Dictionary<string, int>
        {
            {@"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", 10}, // クレジットカード
            {@"\b\d{3}-\d{2}-\d{4}\b", 9}, // SSN
            {@"\b[A-Za-z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}\b", 7}, // メール
            {@"\b[A-Za-z0-9]{32,}\b", 8}, // APIキー
            {@"(password|pwd|passwd|secret|token|key)\s*[:=]\s*\S+", 9}, // パスワード
            {@"[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9\-_.]+", 6}, // JWTトークン
            {@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", 5} // IPアドレス
        };

        foreach (var pattern in patterns)
        {
            if (Regex.IsMatch(content, pattern.Key))
            {
                result.SensitivityScore += pattern.Value;
                result.DetectedPatterns.Add(pattern.Key);
            }
        }

        // スコアに基づくリスクレベル判定
        if (result.SensitivityScore >= 20)
        {
            result.RiskLevel = RiskLevel.Critical;
            result.Recommendations.Add("即時暗号化を推奨");
            result.Recommendations.Add("アクセス制御を強化");
        }
        else if (result.SensitivityScore >= 15)
        {
            result.RiskLevel = RiskLevel.High;
            result.Recommendations.Add("暗号化を強く推奨");
            result.Recommendations.Add("ログ記録を強化");
        }
        else if (result.SensitivityScore >= 10)
        {
            result.RiskLevel = RiskLevel.Medium;
            result.Recommendations.Add("暗号化を推奨");
            result.Recommendations.Add("アクセスログを記録");
        }
        else if (result.SensitivityScore >= 5)
        {
            result.RiskLevel = RiskLevel.Low;
            result.Recommendations.Add("監視を検討");
        }
        else
        {
            result.RiskLevel = RiskLevel.None;
            result.Recommendations.Add("通常のデータとして扱って問題なし");
        }

        if (result.DetectedPatterns.Any())
        {
            await Logger.LogSecurityEventAsync("DataClassified",
                $"Data classified as {result.RiskLevel} (Score: {result.SensitivityScore})");
        }

        return result;
    }

    /// <summary>
    /// データ保持ポリシーに基づいてデータを処理します
    /// </summary>
    public static async Task<DataRetentionResult> ApplyDataRetentionPolicyAsync(string filePath, DataRetentionPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        if (!SecurityUtilities.IsValidFilePath(filePath))
            throw new UnauthorizedAccessException("Invalid file path");

        var result = new DataRetentionResult
        {
            FilePath = filePath,
            PolicyApplied = policy,
            ProcessingTime = DateTime.UtcNow
        };

        try
        {
            var fileInfo = new FileInfo(filePath);

            switch (policy.Action)
            {
                case RetentionAction.Archive:
                    // 暗号化してアーカイブ
                    var archivePath = Path.Combine(policy.ArchiveLocation, $"{Path.GetFileNameWithoutExtension(filePath)}_{DateTime.UtcNow:yyyyMMddHHmmss}.encrypted");
                    await EncryptFileAsync(filePath, archivePath, policy.EncryptionPassword);
                    result.ActionTaken = "Archived and encrypted";
                    result.NewLocation = archivePath;
                    break;

                case RetentionAction.Delete:
                    // セキュア削除
                    await SecurityUtilities.SecureDeleteFileAsync(filePath);
                    result.ActionTaken = "Securely deleted";
                    break;

                case RetentionAction.Encrypt:
                    // その場で暗号化
                    var encryptedPath = filePath + ".encrypted";
                    await EncryptFileAsync(filePath, encryptedPath, policy.EncryptionPassword);
                    File.Delete(filePath); // 元ファイルを削除
                    result.ActionTaken = "Encrypted in place";
                    result.NewLocation = encryptedPath;
                    break;

                case RetentionAction.Anonymize:
                    // データの匿名化
                    var content = await File.ReadAllTextAsync(filePath);
                    var protectedContent = await DetectAndProtectSensitiveDataAsync(content);
                    await File.WriteAllTextAsync(filePath, protectedContent.ProtectedContent);
                    result.ActionTaken = "Anonymized";
                    break;

                default:
                    result.ActionTaken = "No action taken";
                    break;
            }

            await Logger.LogSecurityEventAsync("DataRetentionApplied",
                $"Data retention policy applied: {policy.Action} on {Path.GetFileName(filePath)}");

            return result;
        }
        catch (Exception ex)
        {
            await Logger.LogSecurityEventAsync("DataRetentionError",
                $"Failed to apply data retention policy: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 機密データ保護結果
    /// </summary>
    public class SensitiveDataProtectionResult
    {
        public string? OriginalContent { get; set; }
        public string? ProtectedContent { get; set; }
        public List<string> DetectedSensitiveTypes { get; set; } = new();
        public List<string> ProtectionApplied { get; set; } = new();
        public bool IsProtected { get; set; }
    }

    /// <summary>
    /// データ分類結果
    /// </summary>
    public class DataClassificationResult
    {
        public int ContentLength { get; set; }
        public DateTime ClassificationTime { get; set; }
        public int SensitivityScore { get; set; }
        public RiskLevel RiskLevel { get; set; }
        public List<string> DetectedPatterns { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// データ保持ポリシー
    /// </summary>
    public class DataRetentionPolicy
    {
        public RetentionAction Action { get; set; }
        public string ArchiveLocation { get; set; } = string.Empty;
        public string EncryptionPassword { get; set; } = string.Empty;
        public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(90);
        public bool CompressBeforeArchiving { get; set; }
    }

    /// <summary>
    /// データ保持結果
    /// </summary>
    public class DataRetentionResult
    {
        public string FilePath { get; set; } = string.Empty;
        public DataRetentionPolicy PolicyApplied { get; set; } = null!;
        public DateTime ProcessingTime { get; set; }
        public string ActionTaken { get; set; } = string.Empty;
        public string? NewLocation { get; set; }
    }

    /// <summary>
    /// 保持アクション
    /// </summary>
    public enum RetentionAction
    {
        Archive,
        Delete,
        Encrypt,
        Anonymize,
        None
    }

    /// <summary>
    /// リスクレベル
    /// </summary>
    public enum RiskLevel
    {
        None,
        Low,
        Medium,
        High,
        Critical
    }
}

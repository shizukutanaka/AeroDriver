using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace AeroDriver.Core;

public static class SecurityUtilities
{
    private static readonly Regex PathTraversalPattern = new(@"\.\.(\\|/)", RegexOptions.Compiled);
    private static readonly Regex SqlInjectionPattern = new(@"(;|\bselect\b|\binsert\b|\bupdate\b|\bdelete\b|\bdrop\b|\bcreate\b|\balter\b|\bexec\b|\bexecute\b|\bsp_\b|\bxp_\b|\bunion\b|\bscript\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex XssPattern = new(@"<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex CommandInjectionPattern = new(@"[;&|`$()]", RegexOptions.Compiled);
    private static readonly Regex SensitiveDataPattern = new(@"\b\d{4}[-\s]?\d{4}[-\s]?\d{4}[-\s]?\d{4}\b|\b\d{3}-\d{2}-\d{4}\b|[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}", RegexOptions.Compiled);
    private static readonly SimpleLogger Logger = new();

    #region Input Validation

    public static bool IsValidFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return false;

        if (PathTraversalPattern.IsMatch(path))
        {
            Logger.LogSecurityEvent("PathTraversalAttempt", $"Invalid path detected: {SanitizeLogInput(path)}");
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);

            if (!Path.IsPathFullyQualified(fullPath))
                return false;

            var allowedDirectories = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppDomain.CurrentDomain.BaseDirectory,
                Path.GetTempPath()
            };

            foreach (var allowedDir in allowedDirectories)
            {
                if (IsPathInsideDirectory(fullPath, allowedDir))
                    return true;
            }

            Logger.LogSecurityEvent("UnauthorizedFileAccess", $"Unauthorized path access attempt: {SanitizeLogInput(path)}");
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogSecurityEvent("PathValidationError", $"Path validation error: {ex.Message}");
            return false;
        }
    }

    public static bool IsValidSqlInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return true;

        var isValid = !SqlInjectionPattern.IsMatch(input);

        if (!isValid)
        {
            Logger.LogSecurityEvent("SqlInjectionAttempt", $"SQL injection detected: {SanitizeLogInput(input)}");
        }

        return isValid;
    }

    public static bool IsValidHtmlInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return true;

        var isValid = !XssPattern.IsMatch(input);

        if (!isValid)
        {
            Logger.LogSecurityEvent("XssAttempt", $"XSS attack detected: {SanitizeLogInput(input)}");
        }

        return isValid;
    }

    public static bool IsValidCommandInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return true;

        var isValid = !CommandInjectionPattern.IsMatch(input);

        if (!isValid)
        {
            Logger.LogSecurityEvent("CommandInjectionAttempt", $"Command injection detected: {SanitizeLogInput(input)}");
        }

        return isValid;
    }

    public static ValidationResult ValidateInput(string input, InputType inputType)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new ValidationResult(false, "Input is empty");
        }

        if (input.Length > 10000)
        {
            return new ValidationResult(false, "Input is too long");
        }

        if (input.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
        {
            return new ValidationResult(false, "Input contains invalid control characters");
        }

        if (input.Contains('\0'))
        {
            return new ValidationResult(false, "Input contains null bytes");
        }

        return inputType switch
        {
            InputType.FilePath => new ValidationResult(IsValidFilePath(input), IsValidFilePath(input) ? "Validation successful" : "Invalid file path"),
            InputType.SqlQuery => new ValidationResult(IsValidSqlInput(input), IsValidSqlInput(input) ? "Validation successful" : "Possible SQL injection detected"),
            InputType.HtmlContent => new ValidationResult(IsValidHtmlInput(input), IsValidHtmlInput(input) ? "Validation successful" : "Possible XSS attack detected"),
            InputType.Command => new ValidationResult(IsValidCommandInput(input), IsValidCommandInput(input) ? "Validation successful" : "Possible command injection detected"),
            InputType.GeneralText => new ValidationResult(true, "Validation successful"),
            _ => new ValidationResult(false, "Unknown input type")
        };
    }

    #endregion

    #region Cryptography

    public static void SecureClear(byte[] buffer)
    {
        if (buffer == null)
            return;

        CryptographicOperations.ZeroMemory(buffer);
    }

    public static void SecureClear(SecureString? secureString)
    {
        secureString?.Dispose();
    }

    public static byte[] GenerateSecureRandomBytes(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        var buffer = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buffer);
        return buffer;
    }

    public static string GenerateSecureToken(int length = 32)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var buffer = new byte[length];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buffer);

        var result = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            result.Append(chars[buffer[i] % chars.Length]);
        }

        SecureClear(buffer);
        return result.ToString();
    }

    public static string GenerateSecureRandomString(int length)
    {
        const string validChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        var result = new StringBuilder(length);

        using var rng = RandomNumberGenerator.Create();
        for (int i = 0; i < length; i++)
        {
            var randomBytes = new byte[1];
            rng.GetBytes(randomBytes);
            result.Append(validChars[randomBytes[0] % validChars.Length]);
        }

        return result.ToString();
    }

    public static bool ConstantTimeEquals(string? a, string? b)
    {
        if (a == null && b == null)
            return true;

        if (a == null || b == null)
            return false;

        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);

        try
        {
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }
        finally
        {
            SecureClear(aBytes);
            SecureClear(bBytes);
        }
    }

    public static string ComputeHash(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToBase64String(hash);
    }

    public static string ComputeHash(string data)
    {
        if (string.IsNullOrEmpty(data))
            throw new ArgumentNullException(nameof(data));

        var bytes = Encoding.UTF8.GetBytes(data);
        try
        {
            return ComputeHash(bytes);
        }
        finally
        {
            SecureClear(bytes);
        }
    }

    public static bool VerifyHash(byte[] data, string expectedHash)
    {
        if (data == null || string.IsNullOrEmpty(expectedHash))
            return false;

        var actualHash = ComputeHash(data);
        return ConstantTimeEquals(actualHash, expectedHash);
    }

    #endregion

    #region Password Management

    public static string HashPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentNullException(nameof(password));

        byte[] salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            100000,
            HashAlgorithmName.SHA256
        );

        byte[] hash = pbkdf2.GetBytes(32);

        byte[] hashBytes = new byte[salt.Length + hash.Length];
        Array.Copy(salt, 0, hashBytes, 0, salt.Length);
        Array.Copy(hash, 0, hashBytes, salt.Length, hash.Length);

        return Convert.ToBase64String(hashBytes);
    }

    public static bool VerifyPassword(string password, string hashedPassword)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
            return false;

        try
        {
            byte[] hashBytes = Convert.FromBase64String(hashedPassword);

            if (hashBytes.Length < 32)
                return false;

            byte[] salt = new byte[16];
            Array.Copy(hashBytes, 0, salt, 0, salt.Length);

            using var pbkdf2 = new Rfc2898DeriveBytes(
                password,
                salt,
                100000,
                HashAlgorithmName.SHA256
            );

            byte[] hash = pbkdf2.GetBytes(32);

            for (int i = 0; i < hash.Length; i++)
            {
                if (hashBytes[i + salt.Length] != hash[i])
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static PasswordStrength ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password))
            return PasswordStrength.VeryWeak;

        var score = 0;

        if (password.Length >= 8) score++;
        if (password.Length >= 12) score++;
        if (Regex.IsMatch(password, @"[a-z]")) score++;
        if (Regex.IsMatch(password, @"[A-Z]")) score++;
        if (Regex.IsMatch(password, @"[0-9]")) score++;
        if (Regex.IsMatch(password, @"[^a-zA-Z0-9]")) score++;

        return (PasswordStrength)score;
    }

    #endregion

    #region Data Sanitization

    public static string SanitizeSensitiveData(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return SensitiveDataPattern.Replace(input, "***");
    }

    public static string SanitizeLogInput(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        return SanitizeSensitiveData(input);
    }

    public static string MaskSensitiveString(string sensitive, int visibleChars = 2)
    {
        if (string.IsNullOrEmpty(sensitive))
            return string.Empty;

        if (sensitive.Length <= visibleChars * 2)
            return new string('*', sensitive.Length);

        return $"{sensitive.Substring(0, visibleChars)}***{sensitive.Substring(sensitive.Length - visibleChars)}";
    }

    #endregion

    #region File Security

    public static async Task<bool> VerifyFileIntegrityAsync(string filePath, string expectedHash)
    {
        try
        {
            if (!IsValidFilePath(filePath))
            {
                return false;
            }

            if (!File.Exists(filePath))
            {
                return false;
            }

            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await Task.Run(() => Convert.ToBase64String(sha256.ComputeHash(stream)));

            return ConstantTimeEquals(hash, expectedHash);
        }
        catch (Exception ex)
        {
            Logger.LogSecurityEvent("FileIntegrityCheckError", $"File integrity check error: {ex.Message}");
            return false;
        }
    }

    public static async Task<bool> SecureDeleteFileAsync(string filePath)
    {
        try
        {
            if (!IsValidFilePath(filePath))
            {
                return false;
            }

            if (!File.Exists(filePath))
            {
                return true;
            }

            var fileInfo = new FileInfo(filePath);

            var randomBytes = new byte[4096];
            using (var rng = RandomNumberGenerator.Create())
            using (var stream = fileInfo.OpenWrite())
            {
                for (int i = 0; i < 3; i++)
                {
                    rng.GetBytes(randomBytes);
                    stream.Position = 0;
                    await stream.WriteAsync(randomBytes, 0, Math.Min(randomBytes.Length, (int)stream.Length));
                }
            }

            fileInfo.Delete();

            Logger.LogSecurityEvent("SecureFileDeletion", $"Secure file deletion executed: {SanitizeLogInput(filePath)}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogSecurityEvent("SecureDeleteError", $"Secure deletion error: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Authorization

    public static bool IsRunningAsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static void RequireAdministrator()
    {
        if (!IsRunningAsAdministrator())
        {
            throw new UnauthorizedAccessException("This operation requires administrator privileges");
        }
    }

    #endregion

    #region Helpers

    private static bool IsPathInsideDirectory(string fullPath, string? directory)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(directory))
            return false;

        try
        {
            var normalizedDirectory = Path.GetFullPath(directory);
            if (!Path.IsPathFullyQualified(normalizedDirectory))
                return false;

            normalizedDirectory = EnsureTrailingSeparator(normalizedDirectory);
            fullPath = EnsureTrailingSeparator(Path.GetFullPath(fullPath));

            return fullPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string EnsureTrailingSeparator(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.EndsWith(Path.DirectorySeparatorChar)
            ? value
            : value + Path.DirectorySeparatorChar;
    }

    #endregion

    public enum InputType
    {
        FilePath,
        SqlQuery,
        HtmlContent,
        Command,
        GeneralText
    }

    public enum PasswordStrength
    {
        VeryWeak = 0,
        Weak = 1,
        Fair = 2,
        Good = 3,
        Strong = 4,
        VeryStrong = 5
    }

    public class ValidationResult
    {
        public bool IsValid { get; }
        public string Message { get; }

        public ValidationResult(bool isValid, string message)
        {
            IsValid = isValid;
            Message = message;
        }
    }

    #endregion

    #region Enhanced Sensitive Data Sanitization

    private static readonly ConcurrentDictionary<string, SanitizationRule> _customSanitizationRules = new();
    private static readonly ConcurrentQueue<SanitizationRecord> _sanitizationHistory = new();
    private static readonly ConcurrentDictionary<SanitizationType, SanitizationMetrics> _sanitizationMetrics = new();
    private static int _maxSanitizationHistorySize = 1000;
    private static bool _automaticSanitizationEnabled = true;

    // 高度な機密データパターン
    private static readonly Regex _creditCardPattern = new(@"\b(?:\d{4}[- ]?){3}\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex _ssnPattern = new(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled);
    private static readonly Regex _emailPattern = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled);
    private static readonly Regex _phonePattern = new(@"\b(?:\+?1[-.\s]?)?\(?([0-9]{3})\)?[-.\s]?([0-9]{3})[-.\s]?([0-9]{4})\b", RegexOptions.Compiled);
    private static readonly Regex _apiKeyPattern = new(@"\b[A-Za-z0-9]{32,}\b", RegexOptions.Compiled); // 32文字以上の英数字（APIキーなど）
    private static readonly Regex _passwordPattern = new(@"(?i)(password|pwd|passwd|secret|token|key|auth)\s*[:=]\s*\S+", RegexOptions.Compiled);
    private static readonly Regex _ipAddressPattern = new(@"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", RegexOptions.Compiled);
    private static readonly Regex _jwtTokenPattern = new(@"\beyJ[A-Za-z0-9-_]+\.eyJ[A-Za-z0-9-_]+\.[A-Za-z0-9-_.]+\b", RegexOptions.Compiled);

    /// <summary>
    /// 自動機密情報サニタイズ機能を有効/無効化
    /// </summary>
    public static void SetAutomaticSanitization(bool enabled)
    {
        _automaticSanitizationEnabled = enabled;
    }

    /// <summary>
    /// 高度な機密情報サニタイズ
    /// </summary>
    public static SanitizationResult SanitizeSensitiveDataAdvanced(string? input, SanitizationLevel level = SanitizationLevel.Standard)
    {
        if (string.IsNullOrEmpty(input))
            return new SanitizationResult { OriginalInput = input, SanitizedInput = input, DetectedPatterns = new List<string>() };

        var result = new SanitizationResult
        {
            OriginalInput = input,
            SanitizedInput = input,
            DetectedPatterns = new List<string>(),
            ChangesMade = false
        };

        switch (level)
        {
            case SanitizationLevel.Minimal:
                // クレジットカードとSSNのみ
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _creditCardPattern, "****-****-****-****", ref result.ChangesMade, ref result.DetectedPatterns);
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _ssnPattern, "***-**-****", ref result.ChangesMade, ref result.DetectedPatterns);
                break;

            case SanitizationLevel.Standard:
                // 標準的な機密データ
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _creditCardPattern, "****-****-****-****", ref result.ChangesMade, ref result.DetectedPatterns);
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _ssnPattern, "***-**-****", ref result.ChangesMade, ref result.DetectedPatterns);
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _emailPattern, "***@***.***", ref result.ChangesMade, ref result.DetectedPatterns);
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _phonePattern, "***-***-****", ref result.ChangesMade, ref result.DetectedPatterns);
                break;

            case SanitizationLevel.Strict:
                // すべての機密データを厳格にサニタイズ
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _creditCardPattern, "****-****-****-****", ref result.ChangesMade, ref result.DetectedPatterns);
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _ssnPattern, "***-**-****", ref result.ChangesMade, ref result.DetectedPatterns);
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _emailPattern, "***@***.***", ref result.ChangesMade, ref result.DetectedPatterns);
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _phonePattern, "***-***-****", ref result.ChangesMade, ref result.DetectedPatterns);
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _apiKeyPattern, "***API_KEY***", ref result.ChangesMade, ref result.DetectedPatterns);
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _passwordPattern, "$1=***", ref result.ChangesMade, ref result.DetectedPatterns);
                result.SanitizedInput = SanitizePattern(result.SanitizedInput, _jwtTokenPattern, "***JWT_TOKEN***", ref result.ChangesMade, ref result.DetectedPatterns);
                break;

            case SanitizationLevel.Custom:
                // カスタムルール適用
                result = ApplyCustomSanitizationRules(input);
                break;
        }

        // サニタイズ履歴に記録
        RecordSanitization(result, level);

        return result;
    }

    /// <summary>
    /// カスタムサニタイズルールを追加
    /// </summary>
    public static void AddCustomSanitizationRule(string ruleName, Regex pattern, string replacement, SanitizationType type = SanitizationType.Custom)
    {
        if (string.IsNullOrWhiteSpace(ruleName))
            throw new ArgumentNullException(nameof(ruleName));

        if (pattern == null)
            throw new ArgumentNullException(nameof(pattern));

        _customSanitizationRules[ruleName] = new SanitizationRule
        {
            Name = ruleName,
            Pattern = pattern,
            Replacement = replacement,
            Type = type,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// ログ出力時の自動サニタイズ
    /// </summary>
    public static string SanitizeForLogging(string? input)
    {
        if (string.IsNullOrEmpty(input) || !_automaticSanitizationEnabled)
            return input;

        return SanitizeSensitiveDataAdvanced(input, SanitizationLevel.Standard).SanitizedInput;
    }

    /// <summary>
    /// 機密情報検出のみ（サニタイズせず）
    /// </summary>
    public static SensitiveDataScanResult ScanForSensitiveData(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return new SensitiveDataScanResult { HasSensitiveData = false, DetectedTypes = new List<SanitizationType>() };

        var result = new SensitiveDataScanResult
        {
            HasSensitiveData = false,
            DetectedTypes = new List<SanitizationType>()
        };

        // 各パターンをチェック
        CheckPattern(input, _creditCardPattern, SanitizationType.CreditCard, ref result);
        CheckPattern(input, _ssnPattern, SanitizationType.SocialSecurityNumber, ref result);
        CheckPattern(input, _emailPattern, SanitizationType.Email, ref result);
        CheckPattern(input, _phonePattern, SanitizationType.PhoneNumber, ref result);
        CheckPattern(input, _apiKeyPattern, SanitizationType.ApiKey, ref result);
        CheckPattern(input, _passwordPattern, SanitizationType.Password, ref result);
        CheckPattern(input, _jwtTokenPattern, SanitizationType.JwtToken, ref result);

        return result;
    }

    /// <summary>
    /// サニタイズ統計レポートを取得
    /// </summary>
    public static SanitizationStatisticsReport GetSanitizationStatistics()
    {
        var report = new SanitizationStatisticsReport
        {
            GeneratedAt = DateTime.UtcNow,
            TotalSanitizations = _sanitizationHistory.Count,
            AutomaticSanitizationEnabled = _automaticSanitizationEnabled,
            MetricsByType = _sanitizationMetrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };

        // 最近のサニタイズを分析
        var recentSanitizations = _sanitizationHistory.Where(s =>
            (DateTime.UtcNow - s.Timestamp) < TimeSpan.FromHours(1)).ToList();

        report.RecentSanitizationCount = recentSanitizations.Count;
        report.RecentSensitiveDataDetected = recentSanitizations.Count(s => s.Result.DetectedPatterns.Any());

        return report;
    }

    /// <summary>
    /// 機密データを安全にクリア
    /// </summary>
    public static void SecureClearString(ref string? data)
    {
        if (data == null) return;

        // 文字列の内容を上書き
        var chars = data.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = '\0';
        }

        // 参照をnullに設定
        data = null;
    }

    private static string SanitizePattern(string input, Regex pattern, string replacement, ref bool changesMade, ref List<string> detectedPatterns)
    {
        if (pattern.IsMatch(input))
        {
            changesMade = true;
            detectedPatterns.Add(pattern.ToString());
            return pattern.Replace(input, replacement);
        }
        return input;
    }

    private static void CheckPattern(string input, Regex pattern, SanitizationType type, ref SensitiveDataScanResult result)
    {
        if (pattern.IsMatch(input))
        {
            result.HasSensitiveData = true;
            result.DetectedTypes.Add(type);
        }
    }

    private static SanitizationResult ApplyCustomSanitizationRules(string input)
    {
        var result = new SanitizationResult
        {
            OriginalInput = input,
            SanitizedInput = input,
            DetectedPatterns = new List<string>(),
            ChangesMade = false
        };

        foreach (var rule in _customSanitizationRules.Values)
        {
            result.SanitizedInput = SanitizePattern(result.SanitizedInput, rule.Pattern, rule.Replacement, ref result.ChangesMade, ref result.DetectedPatterns);
        }

        return result;
    }

    private static void RecordSanitization(SanitizationResult result, SanitizationLevel level)
    {
        var record = new SanitizationRecord
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            InputLength = result.OriginalInput?.Length ?? 0,
            OutputLength = result.SanitizedInput?.Length ?? 0,
            Result = result,
            ChangesMade = result.ChangesMade
        };

        _sanitizationHistory.Enqueue(record);

        // 履歴サイズを制限
        while (_sanitizationHistory.Count > _maxSanitizationHistorySize)
        {
            _sanitizationHistory.TryDequeue(out _);
        }

        // メトリクス更新
        foreach (var pattern in result.DetectedPatterns)
        {
            var type = GetSanitizationTypeFromPattern(pattern);
            var metrics = _sanitizationMetrics.GetOrAdd(type, _ => new SanitizationMetrics
            {
                Type = type
            });

            metrics.DetectionCount++;
            metrics.LastDetection = record.Timestamp;
        }
    }

    private static SanitizationType GetSanitizationTypeFromPattern(string pattern)
    {
        // パターン文字列からタイプを推定
        if (pattern.Contains("credit")) return SanitizationType.CreditCard;
        if (pattern.Contains("ssn")) return SanitizationType.SocialSecurityNumber;
        if (pattern.Contains("email")) return SanitizationType.Email;
        if (pattern.Contains("phone")) return SanitizationType.PhoneNumber;
        if (pattern.Contains("api")) return SanitizationType.ApiKey;
        if (pattern.Contains("password")) return SanitizationType.Password;
        if (pattern.Contains("jwt")) return SanitizationType.JwtToken;

        return SanitizationType.Custom;
    }

    /// <summary>
    /// サニタイズレベル
    /// </summary>
    public enum SanitizationLevel
    {
        Minimal,    // クレジットカードとSSNのみ
        Standard,   // 一般的な機密データ
        Strict,     // すべての機密データを厳格に
        Custom      // カスタムルール適用
    }

    /// <summary>
    /// サニタイズタイプ
    /// </summary>
    public enum SanitizationType
    {
        CreditCard,
        SocialSecurityNumber,
        Email,
        PhoneNumber,
        ApiKey,
        Password,
        JwtToken,
        IpAddress,
        Custom
    }

    /// <summary>
    /// サニタイズ結果
    /// </summary>
    public class SanitizationResult
    {
        public string? OriginalInput { get; set; }
        public string? SanitizedInput { get; set; }
        public bool ChangesMade { get; set; }
        public List<string> DetectedPatterns { get; set; } = new();
    }

    /// <summary>
    /// 機密データスキャン結果
    /// </summary>
    public class SensitiveDataScanResult
    {
        public bool HasSensitiveData { get; set; }
        public List<SanitizationType> DetectedTypes { get; set; } = new();
    }

    /// <summary>
    /// サニタイズ統計レポート
    /// </summary>
    public class SanitizationStatisticsReport
    {
        public DateTime GeneratedAt { get; set; }
        public int TotalSanitizations { get; set; }
        public int RecentSanitizationCount { get; set; }
        public int RecentSensitiveDataDetected { get; set; }
        public bool AutomaticSanitizationEnabled { get; set; }
        public Dictionary<SanitizationType, SanitizationMetrics> MetricsByType { get; set; } = new();
    }

    /// <summary>
    /// サニタイズレコード
    /// </summary>
    private class SanitizationRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public SanitizationLevel Level { get; set; }
        public int InputLength { get; set; }
        public int OutputLength { get; set; }
        public SanitizationResult Result { get; set; } = null!;
        public bool ChangesMade { get; set; }
    }

    /// <summary>
    /// サニタイズルール
    /// </summary>
    private class SanitizationRule
    {
        public string Name { get; set; } = string.Empty;
        public Regex Pattern { get; set; } = null!;
        public string Replacement { get; set; } = string.Empty;
        public SanitizationType Type { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// サニタイズメトリクス
    /// </summary>
    private class SanitizationMetrics
    {
        public SanitizationType Type { get; set; }
        public int DetectionCount { get; set; }
        public DateTime LastDetection { get; set; }
    }

    #endregion
}

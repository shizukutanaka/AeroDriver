// This file has been created as part of security enhancement feature implementation
// It provides comprehensive security capabilities including encryption, access control, and audit logging

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Security;

/// <summary>
/// セキュリティマネージャー
/// 暗号化、アクセス制御、監査ログなどのセキュリティ機能を提供
/// </summary>
public static class SecurityManager
{
    private static readonly ConcurrentDictionary<string, UserSession> _activeSessions = new();
    private static readonly ConcurrentDictionary<string, SecurityEvent> _securityEvents = new();
    private static readonly ConcurrentQueue<AuditEntry> _auditLog = new();
    private static readonly Timer _sessionCleanupTimer;
    private static readonly Timer _securityAuditTimer;

    private static readonly Aes _aes = Aes.Create();
    private static readonly SHA256 _sha256 = SHA256.Create();
    private static readonly RSACryptoServiceProvider _rsa = new(2048);

    static SecurityManager()
    {
        _sessionCleanupTimer = new Timer(_ => CleanupExpiredSessions(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        _securityAuditTimer = new Timer(_ => PerformSecurityAudit(), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        // RSA鍵ペアの生成
        var rsaParameters = _rsa.ExportParameters(true);
        // 実際の実装では安全な場所に鍵を保存
    }

    /// <summary>
    /// データを暗号化
    /// </summary>
    public static string EncryptData(string plainText, string key = null)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        using var aes = Aes.Create();
        aes.Key = string.IsNullOrEmpty(key) ? _aes.Key : Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));
        aes.IV = _aes.IV;

        using var encryptor = aes.CreateEncryptor();
        using var ms = new MemoryStream();
        using var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
        using var sw = new StreamWriter(cs);

        sw.Write(plainText);
        sw.Close();

        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// データを復号化
    /// </summary>
    public static string DecryptData(string encryptedText, string key = null)
    {
        if (string.IsNullOrEmpty(encryptedText)) return encryptedText;

        try
        {
            using var aes = Aes.Create();
            aes.Key = string.IsNullOrEmpty(key) ? _aes.Key : Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));
            aes.IV = _aes.IV;

            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream(Convert.FromBase64String(encryptedText));
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);

            return sr.ReadToEnd();
        }
        catch
        {
            // 復号化失敗時は空文字を返す（セキュリティのため）
            return string.Empty;
        }
    }

    /// <summary>
    /// データをRSA暗号化
    /// </summary>
    public static string EncryptWithRSA(string plainText, string publicKeyXml = null)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        try
        {
            var rsa = publicKeyXml != null ? CreateRSAFromPublicKey(publicKeyXml) : _rsa;
            var data = Encoding.UTF8.GetBytes(plainText);
            var encryptedData = rsa.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
            return Convert.ToBase64String(encryptedData);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// データをRSA復号化
    /// </summary>
    public static string DecryptWithRSA(string encryptedText, string privateKeyXml = null)
    {
        if (string.IsNullOrEmpty(encryptedText)) return encryptedText;

        try
        {
            var rsa = privateKeyXml != null ? CreateRSAFromPrivateKey(privateKeyXml) : _rsa;
            var data = Convert.FromBase64String(encryptedText);
            var decryptedData = rsa.Decrypt(data, RSAEncryptionPadding.OaepSHA256);
            return Encoding.UTF8.GetString(decryptedData);
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// パスワードをハッシュ化
    /// </summary>
    public static string HashPassword(string password, string salt = null)
    {
        if (string.IsNullOrEmpty(password)) return string.Empty;

        var saltBytes = string.IsNullOrEmpty(salt)
            ? GenerateSalt()
            : Convert.FromBase64String(salt);

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var combinedBytes = new byte[saltBytes.Length + passwordBytes.Length];

        Buffer.BlockCopy(saltBytes, 0, combinedBytes, 0, saltBytes.Length);
        Buffer.BlockCopy(passwordBytes, 0, combinedBytes, saltBytes.Length, passwordBytes.Length);

        var hashBytes = _sha256.ComputeHash(combinedBytes);
        var hashWithSalt = new byte[saltBytes.Length + hashBytes.Length];

        Buffer.BlockCopy(saltBytes, 0, hashWithSalt, 0, saltBytes.Length);
        Buffer.BlockCopy(hashBytes, 0, hashWithSalt, saltBytes.Length, hashBytes.Length);

        return Convert.ToBase64String(hashWithSalt);
    }

    /// <summary>
    /// パスワードを検証
    /// </summary>
    public static bool VerifyPassword(string password, string hashedPassword)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
            return false;

        try
        {
            var hashWithSaltBytes = Convert.FromBase64String(hashedPassword);
            var saltLength = 32; // 256ビットsalt

            if (hashWithSaltBytes.Length < saltLength) return false;

            var salt = new byte[saltLength];
            var hash = new byte[hashWithSaltBytes.Length - saltLength];

            Buffer.BlockCopy(hashWithSaltBytes, 0, salt, 0, saltLength);
            Buffer.BlockCopy(hashWithSaltBytes, saltLength, hash, 0, hash.Length);

            var computedHash = HashPassword(password, Convert.ToBase64String(salt));

            return hashedPassword == computedHash;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// ユーザーセッションを作成
    /// </summary>
    public static UserSession CreateSession(string userId, string[] roles, TimeSpan? expiration = null)
    {
        var session = new UserSession
        {
            SessionId = Guid.NewGuid().ToString(),
            UserId = userId,
            Roles = roles,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow + (expiration ?? TimeSpan.FromHours(8)),
            IsActive = true
        };

        _activeSessions[session.SessionId] = session;

        AuditAsync("SessionCreated", userId, $"User session created", new { SessionId = session.SessionId });

        return session;
    }

    /// <summary>
    /// セッションを検証
    /// </summary>
    public static bool ValidateSession(string sessionId)
    {
        if (_activeSessions.TryGetValue(sessionId, out var session))
        {
            if (session.IsActive && session.ExpiresAt > DateTime.UtcNow)
            {
                session.LastActivity = DateTime.UtcNow;
                return true;
            }
            else
            {
                // 期限切れまたは無効なセッションを削除
                _activeSessions.TryRemove(sessionId, out _);
            }
        }

        return false;
    }

    /// <summary>
    /// セッションを終了
    /// </summary>
    public static void TerminateSession(string sessionId)
    {
        if (_activeSessions.TryRemove(sessionId, out var session))
        {
            AuditAsync("SessionTerminated", session.UserId, $"User session terminated", new { SessionId = sessionId });
        }
    }

    /// <summary>
    /// アクセス権限を確認
    /// </summary>
    public static bool CheckPermission(string sessionId, string permission)
    {
        if (!ValidateSession(sessionId)) return false;

        if (_activeSessions.TryGetValue(sessionId, out var session))
        {
            // ロールベースの権限チェック
            return session.Roles.Any(role => HasRolePermission(role, permission));
        }

        return false;
    }

    /// <summary>
    /// セキュリティイベントを記録
    /// </summary>
    public static void LogSecurityEvent(string eventType, string userId, string details, SecurityLevel level = SecurityLevel.Info)
    {
        var securityEvent = new SecurityEvent
        {
            Id = Guid.NewGuid().ToString(),
            EventType = eventType,
            UserId = userId,
            Details = details,
            Timestamp = DateTime.UtcNow,
            Level = level,
            IpAddress = GetClientIpAddress(),
            UserAgent = GetUserAgent()
        };

        _securityEvents[securityEvent.Id] = securityEvent;

        // 高レベルセキュリティイベントの場合は即時対応
        if (level >= SecurityLevel.Warning)
        {
            HandleSecurityAlert(securityEvent);
        }

        AuditAsync("SecurityEvent", userId, $"Security event: {eventType} - {details}", new { EventId = securityEvent.Id, Level = level });
    }

    /// <summary>
    /// セキュリティレポートを生成
    /// </summary>
    public static SecurityReport GenerateSecurityReport(TimeSpan timeRange)
    {
        var cutoffTime = DateTime.UtcNow - timeRange;
        var events = _securityEvents.Values.Where(e => e.Timestamp >= cutoffTime).ToArray();
        var auditEntries = _auditLog.Where(a => a.Timestamp >= cutoffTime).ToArray();

        var report = new SecurityReport
        {
            GeneratedAt = DateTime.UtcNow,
            TimeRange = timeRange,
            TotalSecurityEvents = events.Length,
            EventsByLevel = events.GroupBy(e => e.Level)
                .ToDictionary(g => g.Key, g => g.Count()),
            EventsByType = events.GroupBy(e => e.EventType)
                .ToDictionary(g => g.Key, g => g.Count()),
            FailedAuthentications = events.Count(e => e.EventType.Contains("Authentication") && e.Level >= SecurityLevel.Warning),
            SuspiciousActivities = events.Where(e => e.Level >= SecurityLevel.Warning).ToList(),
            ActiveSessions = _activeSessions.Count(s => s.Value.IsActive),
            AuditEntriesCount = auditEntries.Length,
            RecentSecurityEvents = events.OrderByDescending(e => e.Timestamp).Take(20).ToList()
        };

        // セキュリティスコアの計算
        report.SecurityScore = CalculateSecurityScore(report);

        // セキュリティ推奨事項の生成
        report.Recommendations = GenerateSecurityRecommendations(report);

        return report;
    }

    /// <summary>
    /// 機密データをサニタイズ
    /// </summary>
    public static string SanitizeSensitiveData(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // クレジットカード番号をマスク
        var sanitized = MaskCreditCardNumbers(input);

        // 社会保障番号をマスク
        sanitized = MaskSocialSecurityNumbers(sanitized);

        // メールアドレスをマスク
        sanitized = MaskEmailAddresses(sanitized);

        // APIキーをマスク
        sanitized = MaskApiKeys(sanitized);

        return sanitized;
    }

    /// <summary>
    /// 入力データを検証
    /// </summary>
    public static AeroDriver.Core.Validation.ValidationResult ValidateInput(string input, AeroDriver.Core.Validation.ValidationRules? rules = null)
    {
        rules ??= AeroDriver.Core.Validation.ValidationRules.Default;

        var result = new AeroDriver.Core.Validation.ValidationResult { IsValid = true };

        if (string.IsNullOrEmpty(input))
        {
            if (rules.AllowEmpty) return result;

            result.IsValid = false;
            result.Errors.Add("Input cannot be empty");
            return result;
        }

        // 長さチェック
        if (input.Length < rules.MinLength)
        {
            result.IsValid = false;
            result.Errors.Add($"Input must be at least {rules.MinLength} characters long");
        }

        if (input.Length > rules.MaxLength)
        {
            result.IsValid = false;
            result.Errors.Add($"Input cannot exceed {rules.MaxLength} characters");
        }

        // 許可文字チェック
        if (!string.IsNullOrEmpty(rules.AllowedCharacters))
        {
            if (!input.All(c => rules.AllowedCharacters.Contains(c)))
            {
                result.IsValid = false;
                result.Errors.Add("Input contains invalid characters");
            }
        }

        // 禁止文字チェック
        if (!string.IsNullOrEmpty(rules.ForbiddenCharacters))
        {
            if (input.Any(c => rules.ForbiddenCharacters.Contains(c)))
            {
                result.IsValid = false;
                result.Errors.Add("Input contains forbidden characters");
            }
        }

        // SQLインジェクション対策
        if (rules.PreventSqlInjection && ContainsSqlInjectionPatterns(input))
        {
            result.IsValid = false;
            result.Errors.Add("Input contains potentially malicious SQL patterns");
        }

        // XSS対策
        if (rules.PreventXss && ContainsXssPatterns(input))
        {
            result.IsValid = false;
            result.Errors.Add("Input contains potentially malicious XSS patterns");
        }

        // セキュリティイベントを記録
        if (!result.IsValid)
        {
            LogSecurityEvent("InputValidationFailed", "system", $"Validation failed for input: {SanitizeSensitiveData(input)}", SecurityLevel.Warning);
        }

        return result;
    }

    #region Private Methods

    private static byte[] GenerateSalt()
    {
        using var rng = RandomNumberGenerator.Create();
        var salt = new byte[32];
        rng.GetBytes(salt);
        return salt;
    }

    private static RSACryptoServiceProvider CreateRSAFromPublicKey(string publicKeyXml)
    {
        var rsa = new RSACryptoServiceProvider();
        rsa.FromXmlString(publicKeyXml);
        return rsa;
    }

    private static RSACryptoServiceProvider CreateRSAFromPrivateKey(string privateKeyXml)
    {
        var rsa = new RSACryptoServiceProvider();
        rsa.FromXmlString(privateKeyXml);
        return rsa;
    }

    private static bool HasRolePermission(string role, string permission)
    {
        // ロールベースの権限マッピング
        var rolePermissions = new Dictionary<string, string[]>
        {
            ["admin"] = new[] { "read", "write", "delete", "admin" },
            ["manager"] = new[] { "read", "write", "manage" },
            ["user"] = new[] { "read", "write" },
            ["viewer"] = new[] { "read" }
        };

        return rolePermissions.TryGetValue(role, out var permissions) &&
               permissions.Contains(permission);
    }

    private static async Task AuditAsync(string action, string userId, string details, object? additionalData = null)
    {
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            Action = action,
            UserId = userId,
            Details = details,
            AdditionalData = additionalData,
            IpAddress = GetClientIpAddress(),
            UserAgent = GetUserAgent()
        };

        _auditLog.Enqueue(entry);

        // 監査ログを制限（最新50000件）
        while (_auditLog.Count > 50000)
        {
            _auditLog.TryDequeue(out _);
        }
    }

    private static void CleanupExpiredSessions()
    {
        var expiredSessions = _activeSessions.Where(kvp => !kvp.Value.IsActive || kvp.Value.ExpiresAt <= DateTime.UtcNow).ToList();

        foreach (var session in expiredSessions)
        {
            _activeSessions.TryRemove(session.Key, out _);
        }

        if (expiredSessions.Any())
        {
            Debug.WriteLine($"Cleaned up {expiredSessions.Count} expired sessions");
        }
    }

    private static void PerformSecurityAudit()
    {
        // 定期的なセキュリティ監査
        var activeSessions = _activeSessions.Count;
        var securityEvents = _securityEvents.Count;

        // 異常な活動を検出
        var recentEvents = _securityEvents.Values.Where(e => e.Timestamp > DateTime.UtcNow.AddHours(-1)).ToList();

        if (recentEvents.Count > 100) // 1時間に100以上のセキュリティイベント
        {
            LogSecurityEvent("SecurityAudit", "system", $"High security event frequency detected: {recentEvents.Count} events in last hour", SecurityLevel.Warning);
        }

        var failedAuthentications = recentEvents.Count(e => e.EventType.Contains("Authentication") && e.Level >= SecurityLevel.Warning);
        if (failedAuthentications > 10) // 1時間に10以上の認証失敗
        {
            LogSecurityEvent("SecurityAudit", "system", $"High authentication failure rate: {failedAuthentications} failures in last hour", SecurityLevel.Critical);
        }

        Debug.WriteLine($"Security audit completed: {activeSessions} active sessions, {securityEvents} total events");
    }

    private static void HandleSecurityAlert(SecurityEvent securityEvent)
    {
        // セキュリティアラートの処理
        // 実際の実装ではメール通知、ログ記録、アラートシステムへの連携など

        Debug.WriteLine($"Security alert: {securityEvent.EventType} - {securityEvent.Level}");
    }

    private static string GetClientIpAddress()
    {
        // 実際の実装ではHTTPコンテキストから取得
        return "127.0.0.1";
    }

    private static string GetUserAgent()
    {
        // 実際の実装ではHTTPコンテキストから取得
        return "AeroDriver/1.0";
    }

    private static double CalculateSecurityScore(SecurityReport report)
    {
        var score = 100.0;

        // セキュリティイベント数による減点
        if (report.TotalSecurityEvents > 1000)
        {
            score -= 30;
        }
        else if (report.TotalSecurityEvents > 500)
        {
            score -= 15;
        }

        // 認証失敗数による減点
        if (report.FailedAuthentications > 50)
        {
            score -= 40;
        }
        else if (report.FailedAuthentications > 20)
        {
            score -= 20;
        }
        else if (report.FailedAuthentications > 5)
        {
            score -= 10;
        }

        // アクティブセッション数による評価（多すぎるのはリスク）
        if (report.ActiveSessions > 1000)
        {
            score -= 20;
        }

        return Math.Max(0, Math.Min(100, score));
    }

    private static List<string> GenerateSecurityRecommendations(SecurityReport report)
    {
        var recommendations = new List<string>();

        if (report.FailedAuthentications > 10)
        {
            recommendations.Add("High number of authentication failures detected. Consider implementing account lockout policies.");
        }

        if (report.TotalSecurityEvents > 500)
        {
            recommendations.Add("High security event frequency. Review security monitoring and alerting.");
        }

        if (report.ActiveSessions > 500)
        {
            recommendations.Add("Large number of active sessions. Consider implementing session limits and timeouts.");
        }

        var criticalEvents = report.EventsByLevel.GetValueOrDefault(SecurityLevel.Critical, 0);
        if (criticalEvents > 0)
        {
            recommendations.Add($"{criticalEvents} critical security events detected. Immediate investigation required.");
        }

        return recommendations;
    }

    private static string MaskCreditCardNumbers(string input)
    {
        // クレジットカード番号のパターン（簡易版）
        var pattern = @"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b";
        return System.Text.RegularExpressions.Regex.Replace(input, pattern, "XXXX-XXXX-XXXX-XXXX");
    }

    private static string MaskSocialSecurityNumbers(string input)
    {
        // 社会保障番号のパターン（簡易版）
        var pattern = @"\b\d{3}[\s-]?\d{2}[\s-]?\d{4}\b";
        return System.Text.RegularExpressions.Regex.Replace(input, pattern, "XXX-XX-XXXX");
    }

    private static string MaskEmailAddresses(string input)
    {
        // メールアドレスをマスク
        var pattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b";
        return System.Text.RegularExpressions.Regex.Replace(input, pattern, "****@****.***");
    }

    private static string MaskApiKeys(string input)
    {
        // APIキーらしき長い英数字文字列をマスク
        var pattern = @"\b[A-Za-z0-9]{32,}\b";
        return System.Text.RegularExpressions.Regex.Replace(input, pattern, "***************************");
    }

    private static bool ContainsSqlInjectionPatterns(string input)
    {
        var patterns = new[]
        {
            "union select", "union all select", "insert into", "delete from", "update.*set",
            "drop table", "drop database", "exec(", "execute(", "script>", "<script",
            "--", "/*", "*/", "xp_", "sp_", "1=1", "1'1", "' or '1'='1"
        };

        return patterns.Any(pattern => input.ToLower().Contains(pattern.ToLower()));
    }

    private static bool ContainsXssPatterns(string input)
    {
        var patterns = new[]
        {
            "<script", "</script>", "javascript:", "vbscript:", "onload=", "onerror=",
            "<iframe", "<object", "<embed", "eval(", "alert(", "document.cookie"
        };

        return patterns.Any(pattern => input.ToLower().Contains(pattern.ToLower()));
    }

    #endregion

    #region Data Classes

    /// <summary>
    /// ユーザーセッション
    /// </summary>
    public class UserSession
    {
        public string SessionId { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string[] Roles { get; set; } = Array.Empty<string>();
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? LastActivity { get; set; }
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// セキュリティイベント
    /// </summary>
    public class SecurityEvent
    {
        public string Id { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public SecurityLevel Level { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
    }

    /// <summary>
    /// 監査エントリ
    /// </summary>
    public class AuditEntry
    {
        public string Id { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public object? AdditionalData { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
    }

    /// <summary>
    /// セキュリティレポート
    /// </summary>
    public class SecurityReport
    {
        public DateTime GeneratedAt { get; set; }
        public TimeSpan TimeRange { get; set; }
        public int TotalSecurityEvents { get; set; }
        public Dictionary<SecurityLevel, int> EventsByLevel { get; set; } = new();
        public Dictionary<string, int> EventsByType { get; set; } = new();
        public int FailedAuthentications { get; set; }
        public List<SecurityEvent> SuspiciousActivities { get; set; } = new();
        public int ActiveSessions { get; set; }
        public int AuditEntriesCount { get; set; }
        public List<SecurityEvent> RecentSecurityEvents { get; set; } = new();
        public double SecurityScore { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }


    /// <summary>
    /// セキュリティレベル
    /// </summary>
    public enum SecurityLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }

    #endregion
}

using System;
using System.Collections.Generic;
using System.Security;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using OtpNet;

namespace AeroDriver.Core.Security;

/// <summary>
/// 多要素認証プロバイダー
/// エンタープライズグレードのMFA実装を提供します
/// </summary>
public class MultiFactorAuthenticationProvider
{
    private readonly Dictionary<string, MfaConfiguration> _userConfigurations = new();
    private readonly Dictionary<string, string> _pendingChallenges = new();
    private readonly ISimpleLogger _logger;
    private readonly AuditTrail _auditTrail;

    public MultiFactorAuthenticationProvider(AuditTrail auditTrail, ISimpleLogger logger)
    {
        _auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// ユーザーのMFAを有効化します
    /// </summary>
    public async Task<(bool Success, string? SecretKey, string? QrCodeUri)> EnableMfaAsync(string userIdentity, MfaMethod method, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity, nameof(userIdentity));

        if (!_userConfigurations.ContainsKey(userIdentity))
        {
            _userConfigurations[userIdentity] = new MfaConfiguration { UserIdentity = userIdentity };
        }

        var config = _userConfigurations[userIdentity];
        config.Method = method;
        config.IsEnabled = true;
        config.LastUpdated = DateTime.UtcNow;

        string? secretKey = null;
        string? qrCodeUri = null;

        if (method == MfaMethod.Totp)
        {
            // TOTP用の秘密鍵を生成
            secretKey = GenerateTotpSecret();
            config.TotpSecret = secretKey;
            qrCodeUri = GenerateTotpQrCodeUri(userIdentity, secretKey);
        }

        await _auditTrail.RecordSecurityEventAsync(
            SecurityEventType.PolicyViolation,
            $"MFA enabled for user: {userIdentity}",
            SecuritySeverity.Medium,
            new Dictionary<string, string> { ["method"] = method.ToString() },
            cancellationToken);

        await _logger.LogSecurityEventAsync("MfaEnabled", $"MFA enabled for user: {userIdentity}");

        return (true, secretKey, qrCodeUri);
    }

    /// <summary>
    /// ユーザーのMFAを無効化します
    /// </summary>
    public async Task DisableMfaAsync(string userIdentity, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity, nameof(userIdentity));
        ArgumentException.ThrowIfNullOrWhiteSpace(reason, nameof(reason));

        if (_userConfigurations.TryGetValue(userIdentity, out var config))
        {
            config.IsEnabled = false;
            config.LastUpdated = DateTime.UtcNow;
        }

        await _auditTrail.RecordSecurityEventAsync(
            SecurityEventType.PolicyViolation,
            $"MFA disabled for user: {userIdentity}. Reason: {reason}",
            SecuritySeverity.High,
            new Dictionary<string, string> { ["reason"] = reason },
            cancellationToken);

        await _logger.LogSecurityEventAsync("MfaDisabled", $"MFA disabled for user: {userIdentity}. Reason: {reason}");
    }

    /// <summary>
    /// MFAチャレンジを開始します
    /// </summary>
    public async Task<string> InitiateMfaChallengeAsync(string userIdentity, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity, nameof(userIdentity));

        if (!_userConfigurations.TryGetValue(userIdentity, out var config) || !config.IsEnabled)
        {
            throw new AuthenticationException($"MFA is not enabled for user: {userIdentity}");
        }

        var challengeId = Guid.NewGuid().ToString();
        var challenge = GenerateChallenge(config.Method);

        _pendingChallenges[challengeId] = challenge;

        await _auditTrail.RecordSecurityEventAsync(
            SecurityEventType.SuspiciousActivity,
            $"MFA challenge initiated for user: {userIdentity}",
            SecuritySeverity.Low,
            new Dictionary<string, string> { ["challengeId"] = challengeId },
            cancellationToken);

        return challengeId;
    }

    /// <summary>
    /// MFAチャレンジを検証します
    /// </summary>
    public async Task<bool> VerifyMfaChallengeAsync(string userIdentity, string challengeId, string response, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity, nameof(userIdentity));
        ArgumentException.ThrowIfNullOrWhiteSpace(challengeId, nameof(challengeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(response, nameof(response));

        if (!_pendingChallenges.TryGetValue(challengeId, out var expectedChallenge))
        {
            await _auditTrail.RecordSecurityEventAsync(
                SecurityEventType.ValidationFailure,
                $"Invalid MFA challenge ID: {challengeId} for user: {userIdentity}",
                SecuritySeverity.Medium,
                cancellationToken: cancellationToken);

            return false;
        }

        if (string.IsNullOrEmpty(expectedChallenge))
        {
            await _auditTrail.RecordSecurityEventAsync(
                SecurityEventType.ValidationFailure,
                $"Empty MFA challenge for user: {userIdentity}",
                SecuritySeverity.Medium,
                cancellationToken: cancellationToken);

            return false;
        }

        var isValid = VerifyResponse(userIdentity, expectedChallenge, response);

        if (isValid)
        {
            // Clean up used challenge
            _pendingChallenges.Remove(challengeId);

            await _auditTrail.RecordSecurityEventAsync(
                SecurityEventType.SuspiciousActivity,
                $"MFA challenge verified successfully for user: {userIdentity}",
                SecuritySeverity.Low,
                new Dictionary<string, string> { ["challengeId"] = challengeId },
                cancellationToken);
        }
        else
        {
            await _auditTrail.RecordSecurityEventAsync(
                SecurityEventType.ValidationFailure,
                $"MFA challenge verification failed for user: {userIdentity}",
                SecuritySeverity.Medium,
                new Dictionary<string, string> { ["challengeId"] = challengeId },
                cancellationToken);
        }

        return isValid;
    }

    /// <summary>
    /// ユーザーのMFA設定を取得します
    /// </summary>
    public MfaConfiguration? GetMfaConfiguration(string userIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userIdentity, nameof(userIdentity));

        return _userConfigurations.TryGetValue(userIdentity, out var config) ? config : null;
    }

    private string GenerateChallenge(MfaMethod method)
    {
        return method switch
        {
            MfaMethod.Totp => GenerateTotpChallenge(),
            MfaMethod.Sms => GenerateSmsChallenge(),
            MfaMethod.Email => GenerateEmailChallenge(),
            MfaMethod.PushNotification => GeneratePushChallenge(),
            _ => throw new NotSupportedException($"MFA method {method} is not supported")
        };
    }

    private string GenerateTotpChallenge()
    {
        // TOTPはチャレンジレスポンス方式なので、空文字列を返す
        return string.Empty;
    }

    private string GenerateTotpSecret()
    {
        // 32バイトのランダムな秘密鍵を生成
        var secretBytes = new byte[32];
        RandomNumberGenerator.Fill(secretBytes);
        return Base32Encoding.ToString(secretBytes);
    }

    private string GenerateTotpQrCodeUri(string userIdentity, string secret)
    {
        // otpauth://totp/Label:Issuer?secret=Secret&issuer=Issuer
        var issuer = "AeroDriver";
        var label = Uri.EscapeDataString($"{issuer}:{userIdentity}");
        return $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}";
    }

    private string GenerateSmsChallenge()
    {
        // SMS実装は別途必要ですが、ここではサンプルとしてランダムコードを生成
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }

    private string GenerateEmailChallenge()
    {
        // Email実装は別途必要ですが、ここではサンプルとしてランダムコードを生成
        var random = new Random();
        return random.Next(100000, 999999).ToString();
    }

    private string GeneratePushChallenge()
    {
        // Push通知実装は別途必要ですが、ここではサンプルとしてUUIDを生成
        return Guid.NewGuid().ToString();
    }

    private bool VerifyResponse(string userIdentity, string challenge, string response)
    {
        if (!_userConfigurations.TryGetValue(userIdentity, out var config))
        {
            return false;
        }

        return config.Method switch
        {
            MfaMethod.Totp => VerifyTotpResponse(config.TotpSecret, response),
            MfaMethod.Sms or MfaMethod.Email or MfaMethod.PushNotification => string.Equals(challenge, response, StringComparison.Ordinal),
            _ => false
        };
    }

    private bool VerifyTotpResponse(string? secret, string response)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return false;
        }

        try
        {
            var secretBytes = Base32Encoding.ToBytes(secret);
            var totp = new Totp(secretBytes);
            return totp.VerifyTotp(response, out _, new VerificationWindow(2, 2)); // 2ステップ前後を許容
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// MFA設定
/// </summary>
public class MfaConfiguration
{
    public string UserIdentity { get; set; } = string.Empty;
    public MfaMethod Method { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime LastUpdated { get; set; }
    public string? BackupCodes { get; set; }
    public string? TotpSecret { get; set; }
}

/// <summary>
/// MFA方法
/// </summary>
public enum MfaMethod
{
    Totp,
    Sms,
    Email,
    PushNotification,
    HardwareToken
}

using System.Security.Claims;
using AeroDriver.Core.Security;
using AeroDriver.Core;

namespace AeroDriver.API.Services;

/// <summary>
/// エンタープライズグレードの認証サービス
/// 多要素認証、ロールベースアクセス制御、監査機能を統合
/// </summary>
public class AuthenticationService
{
    private readonly JwtTokenService _jwtTokenService;
    private readonly MultiFactorAuthenticationProvider _mfaProvider;
    private readonly AuditTrail _auditTrail;
    private readonly ISimpleLogger _logger;

    public AuthenticationService(
        JwtTokenService jwtTokenService,
        MultiFactorAuthenticationProvider mfaProvider,
        AuditTrail auditTrail,
        ISimpleLogger logger)
    {
        _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
        _mfaProvider = mfaProvider ?? throw new ArgumentNullException(nameof(mfaProvider));
        _auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// ユーザーの認証を実行
    /// </summary>
    public async Task<AuthenticationResult> AuthenticateUserAsync(
        string username,
        string password,
        string? twoFactorCode = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 基本的な入力検証
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return new AuthenticationResult
                {
                    Success = false,
                    Message = "Username and password are required"
                };
            }

            // ここではデモとして固定ユーザーの検証（実際の実装ではデータベースやLDAPを使用）
            if (!ValidateCredentials(username, password))
            {
                await _auditTrail.RecordSecurityEventAsync(
                    SecurityEventType.AuthenticationFailure,
                    $"Failed login attempt for user: {username}",
                    SecuritySeverity.Medium,
                    new Dictionary<string, string> { ["username"] = username },
                    cancellationToken);

                return new AuthenticationResult
                {
                    Success = false,
                    Message = "Invalid username or password"
                };
            }

            // MFAが有効な場合は検証
            if (!string.IsNullOrEmpty(twoFactorCode))
            {
                var mfaResult = await _mfaProvider.VerifyMfaChallengeAsync(
                    username,
                    twoFactorCode, // 簡易的にチャレンジIDとして使用
                    twoFactorCode,
                    cancellationToken);

                if (!mfaResult)
                {
                    await _auditTrail.RecordSecurityEventAsync(
                        SecurityEventType.AuthenticationFailure,
                        $"MFA verification failed for user: {username}",
                        SecuritySeverity.High,
                        new Dictionary<string, string> { ["username"] = username },
                        cancellationToken);

                    return new AuthenticationResult
                    {
                        Success = false,
                        Message = "Invalid two-factor authentication code"
                    };
                }
            }
            else
            {
                // MFAが必要な場合はチャレンジを開始
                var mfaConfig = _mfaProvider.GetMfaConfiguration(username);
                if (mfaConfig?.IsEnabled == true)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        Message = "Two-factor authentication required",
                        RequiresTwoFactor = true
                    };
                }
            }

            // ロール情報の取得（デモとして固定ロールを使用）
            var roles = GetUserRoles(username);

            // JWTトークンの生成
            var token = _jwtTokenService.GenerateToken(
                username, // userIdとしてusernameを使用
                username,
                roles,
                TimeSpan.FromHours(8)); // 8時間の有効期限

            await _auditTrail.RecordSecurityEventAsync(
                SecurityEventType.AuthenticationSuccess,
                $"Successful login for user: {username}",
                SecuritySeverity.Low,
                new Dictionary<string, string>
                {
                    ["username"] = username,
                    ["roles"] = string.Join(",", roles)
                },
                cancellationToken);

            return new AuthenticationResult
            {
                Success = true,
                Message = "Authentication successful",
                Token = token,
                Username = username,
                Roles = roles
            };
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Authentication error for user {username}: {ex.Message}");
            return new AuthenticationResult
            {
                Success = false,
                Message = "An error occurred during authentication"
            };
        }
    }

    /// <summary>
    /// トークンの検証
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            return _jwtTokenService.ValidateToken(token);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Token validation error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// ユーザーロールの取得
    /// </summary>
    private List<string> GetUserRoles(string username)
    {
        // デモ実装：実際の実装ではデータベースやLDAPから取得
        return username.ToLower() switch
        {
            "admin" => new List<string> { "Administrator", "User" },
            "manager" => new List<string> { "Manager", "User" },
            _ => new List<string> { "User" }
        };
    }

    /// <summary>
    /// 認証情報の検証
    /// </summary>
    private bool ValidateCredentials(string username, string password)
    {
        // デモ実装：実際の実装ではデータベースやLDAPを使用
        // セキュリティのため、本番環境では適切なハッシュ化されたパスワード検証を実装
        return username.ToLower() switch
        {
            "admin" => password == "AdminPass123!", // デモ用
            "manager" => password == "ManagerPass123!", // デモ用
            "user" => password == "UserPass123!", // デモ用
            _ => false
        };
    }

    /// <summary>
    /// MFA設定の有効化
    /// </summary>
    public async Task<(bool Success, string? SecretKey, string? QrCodeUri)> EnableMfaAsync(
        string username,
        MfaMethod method,
        CancellationToken cancellationToken = default)
    {
        return await _mfaProvider.EnableMfaAsync(username, method, cancellationToken);
    }

    /// <summary>
    /// MFA設定の無効化
    /// </summary>
    public async Task DisableMfaAsync(
        string username,
        string reason,
        CancellationToken cancellationToken = default)
    {
        await _mfaProvider.DisableMfaAsync(username, reason, cancellationToken);
    }
}

/// <summary>
/// 認証結果
/// </summary>
public class AuthenticationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Token { get; set; }
    public string? Username { get; set; }
    public List<string>? Roles { get; set; }
    public bool RequiresTwoFactor { get; set; }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AeroDriver.API.Services;

/// <summary>
/// JWTトークンサービス - エンタープライズグレードの認証機能を提供
/// </summary>
public class JwtTokenService
{
    private readonly IConfiguration _configuration;
    private readonly ISimpleLogger _logger;

    public JwtTokenService(IConfiguration configuration, ISimpleLogger logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// JWTトークンを生成
    /// </summary>
    public string GenerateToken(string userId, string username, List<string> roles, TimeSpan expiration)
    {
        try
        {
            var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is not configured");
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? "Otedama";
            var jwtAudience = _configuration["Jwt:Audience"] ?? "OtedamaUsers";

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Name, username),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                new Claim(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow.Add(expiration).ToUnixTimeSeconds().ToString()),
                new Claim(JwtRegisteredClaimNames.Iss, jwtIssuer),
                new Claim(JwtRegisteredClaimNames.Aud, jwtAudience)
            };

            // ロール情報を追加
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: DateTime.UtcNow.Add(expiration),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to generate JWT token: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// JWTトークンを検証
    /// </summary>
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is not configured");
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? "Otedama";
            var jwtAudience = _configuration["Jwt:Audience"] ?? "OtedamaUsers";

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtIssuer,
                ValidAudience = jwtAudience,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(2) // 許容する時間差
            };

            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);

            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"JWT token validation failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// リフレッシュトークンを生成
    /// </summary>
    public string GenerateRefreshToken()
    {
        return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// トークンからユーザー情報を抽出
    /// </summary>
    public UserTokenInfo? ExtractUserInfo(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);

            return new UserTokenInfo
            {
                UserId = jwtToken.Subject,
                Username = jwtToken.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Name)?.Value ?? "",
                Roles = jwtToken.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList(),
                ExpiresAt = jwtToken.ValidTo,
                IssuedAt = jwtToken.IssuedAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to extract user info from token: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// ユーザートークン情報
/// </summary>
public class UserTokenInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public DateTime ExpiresAt { get; set; }
    public DateTime IssuedAt { get; set; }
}

/// <summary>
/// 認証サービス - 多要素認証を含む包括的な認証機能
/// </summary>
public class AuthenticationService
{
    private readonly JwtTokenService _jwtTokenService;
    private readonly ISimpleLogger _logger;
    private readonly Dictionary<string, UserSession> _activeSessions = new();

    public AuthenticationService(JwtTokenService jwtTokenService, ISimpleLogger logger)
    {
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    /// <summary>
    /// ユーザーを認証
    /// </summary>
    public async Task<AuthenticationResult> AuthenticateUserAsync(string username, string password, string? twoFactorCode = null)
    {
        // 実際の実装ではデータベースやActive Directoryとの連携が必要
        // ここでは簡易的な実装を示す

        try
        {
            // ユーザー検証（実際にはデータベースやADから取得）
            var user = await ValidateUserCredentialsAsync(username, password);

            if (user == null)
            {
                await _logger.LogSecurityEventAsync("AuthenticationFailure",
                    $"Failed login attempt for user: {username}");
                return new AuthenticationResult { Success = false, Message = "Invalid credentials" };
            }

            // 二要素認証確認（設定されている場合）
            if (user.TwoFactorEnabled && string.IsNullOrEmpty(twoFactorCode))
            {
                return new AuthenticationResult
                {
                    Success = false,
                    RequiresTwoFactor = true,
                    Message = "Two-factor authentication required"
                };
            }

            if (user.TwoFactorEnabled && !string.IsNullOrEmpty(twoFactorCode))
            {
                var twoFactorValid = await ValidateTwoFactorCodeAsync(user.UserId, twoFactorCode);
                if (!twoFactorValid)
                {
                    await _logger.LogSecurityEventAsync("TwoFactorFailure",
                        $"Invalid 2FA code for user: {username}");
                    return new AuthenticationResult { Success = false, Message = "Invalid two-factor code" };
                }
            }

            // JWTトークン生成
            var accessToken = _jwtTokenService.GenerateToken(
                user.UserId,
                user.Username,
                user.Roles,
                TimeSpan.FromHours(1) // アクセストークンの有効期限
            );

            var refreshToken = _jwtTokenService.GenerateRefreshToken();

            // セッション情報保存
            var session = new UserSession
            {
                SessionId = Guid.NewGuid().ToString(),
                UserId = user.UserId,
                Username = user.Username,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                LastActivity = DateTime.UtcNow
            };

            _activeSessions[session.SessionId] = session;

            await _logger.LogSecurityEventAsync("AuthenticationSuccess",
                $"Successful login for user: {username}");

            return new AuthenticationResult
            {
                Success = true,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = 3600, // 1時間
                User = user
            };
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Authentication error: {ex.Message}");
            return new AuthenticationResult { Success = false, Message = "Authentication failed" };
        }
    }

    /// <summary>
    /// リフレッシュトークンを使用して新しいアクセストークンを取得
    /// </summary>
    public async Task<RefreshTokenResult> RefreshAccessTokenAsync(string refreshToken)
    {
        // 実際の実装ではデータベースからリフレッシュトークンを検証
        // ここでは簡易的な実装を示す

        if (string.IsNullOrEmpty(refreshToken))
        {
            return new RefreshTokenResult { Success = false, Message = "Invalid refresh token" };
        }

        // セッション検証
        var session = _activeSessions.Values.FirstOrDefault(s => s.RefreshToken == refreshToken);
        if (session == null || session.ExpiresAt < DateTime.UtcNow)
        {
            return new RefreshTokenResult { Success = false, Message = "Invalid or expired refresh token" };
        }

        // 新しいアクセストークン生成
        var newAccessToken = _jwtTokenService.GenerateToken(
            session.UserId,
            session.Username,
            session.Roles,
            TimeSpan.FromHours(1)
        );

        session.AccessToken = newAccessToken;
        session.LastActivity = DateTime.UtcNow;
        session.ExpiresAt = DateTime.UtcNow.AddHours(1);

        return new RefreshTokenResult
        {
            Success = true,
            AccessToken = newAccessToken,
            ExpiresIn = 3600
        };
    }

    /// <summary>
    /// セッションをログアウト
    /// </summary>
    public void Logout(string sessionId)
    {
        _activeSessions.Remove(sessionId);
    }

    /// <summary>
    /// ユーザー資格情報を検証（実際の実装ではデータベースやADと連携）
    /// </summary>
    private async Task<UserInfo?> ValidateUserCredentialsAsync(string username, string password)
    {
        // 実際の実装ではデータベースやActive Directoryとの連携が必要
        // ここでは簡易的な実装を示す

        await Task.Delay(100); // タイミング攻撃対策のための遅延

        // デモ用の簡易検証（実際には適切なパスワード検証を使用）
        if (username == "admin" && password == "SecurePassword123!")
        {
            return new UserInfo
            {
                UserId = "admin-001",
                Username = "admin",
                Email = "admin@otedama.local",
                Roles = new List<string> { "Administrator" },
                TwoFactorEnabled = false
            };
        }

        if (username == "operator" && password == "OperatorPass456!")
        {
            return new UserInfo
            {
                UserId = "operator-001",
                Username = "operator",
                Email = "operator@otedama.local",
                Roles = new List<string> { "Operator" },
                TwoFactorEnabled = true
            };
        }

        return null;
    }

    /// <summary>
    /// 二要素認証コードを検証（実際の実装ではTOTPなどの標準的な2FAを使用）
    /// </summary>
    private async Task<bool> ValidateTwoFactorCodeAsync(string userId, string code)
    {
        // 実際の実装ではTOTP（Time-based One-Time Password）などの標準的な2FAを使用
        // ここでは簡易的な実装を示す

        await Task.Delay(50);

        // デモ用の簡易検証（実際には適切なTOTP検証を使用）
        return code == "123456";
    }
}

/// <summary>
/// 認証結果
/// </summary>
public class AuthenticationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool RequiresTwoFactor { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public int ExpiresIn { get; set; }
    public UserInfo? User { get; set; }
}

/// <summary>
/// リフレッシュトークン結果
/// </summary>
public class RefreshTokenResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public int ExpiresIn { get; set; }
}

/// <summary>
/// ユーザー情報
/// </summary>
public class UserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public bool TwoFactorEnabled { get; set; }
}

/// <summary>
/// ユーザーセッション情報
/// </summary>
public class UserSession
{
    public string SessionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastActivity { get; set; }
}

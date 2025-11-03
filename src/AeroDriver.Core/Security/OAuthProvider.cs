using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.IdentityModel.Tokens;

namespace AeroDriver.Core.Security;

/// <summary>
/// OAuth 2.0 / OpenID Connect プロバイダー
/// エンタープライズグレードのOAuth 2.0およびOIDC実装を提供します
/// </summary>
public class OAuthProvider
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly ISimpleLogger _logger;
    private readonly AuditTrail _auditTrail;

    // 認可コードの保存（本番環境ではデータベースを使用）
    private readonly Dictionary<string, AuthorizationCode> _authorizationCodes = new();
    private readonly Dictionary<string, string> _refreshTokens = new();

    public OAuthProvider(string issuer, string audience, string signingKeySecret, AuditTrail auditTrail, ISimpleLogger logger)
    {
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _audience = audience ?? throw new ArgumentNullException(nameof(audience));
        _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKeySecret));
        _auditTrail = auditTrail ?? throw new ArgumentNullException(nameof(auditTrail));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 認可コードフローを開始します
    /// </summary>
    public async Task<AuthorizationResponse> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 認可コードを生成
        var code = GenerateAuthorizationCode();
        var authorizationCode = new AuthorizationCode
        {
            Code = code,
            ClientId = request.ClientId,
            UserId = request.UserId,
            Scopes = request.Scopes,
            RedirectUri = request.RedirectUri,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10), // 10分で期限切れ
            CodeChallenge = request.CodeChallenge,
            CodeChallengeMethod = request.CodeChallengeMethod
        };

        _authorizationCodes[code] = authorizationCode;

        await _auditTrail.RecordSecurityEventAsync(
            SecurityEventType.SuspiciousActivity,
            $"Authorization code issued for client: {request.ClientId}, user: {request.UserId}",
            SecuritySeverity.Low,
            new Dictionary<string, string>
            {
                ["client_id"] = request.ClientId,
                ["user_id"] = request.UserId,
                ["scopes"] = string.Join(",", request.Scopes)
            },
            cancellationToken);

        return new AuthorizationResponse
        {
            Code = code,
            State = request.State
        };
    }

    /// <summary>
    /// トークンエンドポイント - 認可コードをトークンと交換
    /// </summary>
    public async Task<TokenResponse> ExchangeCodeForTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 認可コードの検証
        if (!_authorizationCodes.TryGetValue(request.Code, out var authorizationCode))
        {
            throw new SecurityException("Invalid authorization code");
        }

        // 認可コードの有効期限チェック
        if (authorizationCode.ExpiresAt < DateTime.UtcNow)
        {
            _authorizationCodes.Remove(request.Code);
            throw new SecurityException("Authorization code expired");
        }

        // PKCE検証
        if (!string.IsNullOrEmpty(authorizationCode.CodeChallenge))
        {
            if (!ValidateCodeChallenge(request.CodeVerifier, authorizationCode.CodeChallenge, authorizationCode.CodeChallengeMethod))
            {
                throw new SecurityException("Invalid code verifier");
            }
        }

        // クライアント認証（簡易版）
        if (request.ClientId != authorizationCode.ClientId)
        {
            throw new SecurityException("Client authentication failed");
        }

        // トークン生成
        var accessToken = GenerateAccessToken(authorizationCode);
        var refreshToken = GenerateRefreshToken();
        var idToken = GenerateIdToken(authorizationCode);

        // リフレッシュトークンを保存
        _refreshTokens[refreshToken] = authorizationCode.UserId;

        // 認可コードを使用済みとして削除
        _authorizationCodes.Remove(request.Code);

        await _auditTrail.RecordSecurityEventAsync(
            SecurityEventType.SuspiciousActivity,
            $"Tokens issued for user: {authorizationCode.UserId}, client: {authorizationCode.ClientId}",
            SecuritySeverity.Low,
            new Dictionary<string, string>
            {
                ["client_id"] = authorizationCode.ClientId,
                ["user_id"] = authorizationCode.UserId,
                ["token_type"] = "Bearer"
            },
            cancellationToken);

        return new TokenResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            IdToken = idToken,
            TokenType = "Bearer",
            ExpiresIn = 3600, // 1時間
            Scope = string.Join(" ", authorizationCode.Scopes)
        };
    }

    /// <summary>
    /// リフレッシュトークンを使用して新しいアクセストークンを取得
    /// </summary>
    public async Task<TokenResponse> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_refreshTokens.TryGetValue(request.RefreshToken, out var userId))
        {
            throw new SecurityException("Invalid refresh token");
        }

        // 新しいアクセストークンを生成
        var accessToken = GenerateAccessToken(new AuthorizationCode { UserId = userId, Scopes = request.Scopes });
        var idToken = GenerateIdToken(new AuthorizationCode { UserId = userId });

        await _auditTrail.RecordSecurityEventAsync(
            SecurityEventType.SuspiciousActivity,
            $"Token refreshed for user: {userId}",
            SecuritySeverity.Low,
            new Dictionary<string, string> { ["user_id"] = userId },
            cancellationToken);

        return new TokenResponse
        {
            AccessToken = accessToken,
            IdToken = idToken,
            TokenType = "Bearer",
            ExpiresIn = 3600
        };
    }

    /// <summary>
    /// アクセストークンを検証
    /// </summary>
    public ClaimsPrincipal ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        try
        {
            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch (Exception ex)
        {
            throw new SecurityException($"Token validation failed: {ex.Message}");
        }
    }

    private string GenerateAuthorizationCode()
    {
        return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }

    private string GenerateAccessToken(AuthorizationCode authorizationCode)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, authorizationCode.UserId),
            new Claim(JwtRegisteredClaimNames.Iss, _issuer),
            new Claim(JwtRegisteredClaimNames.Aud, _audience),
            new Claim(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
        };

        // スコープをクレームに追加
        foreach (var scope in authorizationCode.Scopes)
        {
            claims.Add(new Claim("scope", scope));
        }

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateRefreshToken()
    {
        return Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }

    private string GenerateIdToken(AuthorizationCode authorizationCode)
    {
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, authorizationCode.UserId),
            new Claim(JwtRegisteredClaimNames.Iss, _issuer),
            new Claim(JwtRegisteredClaimNames.Aud, authorizationCode.ClientId),
            new Claim(JwtRegisteredClaimNames.Exp, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
            new Claim("preferred_username", authorizationCode.UserId)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: authorizationCode.ClientId,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private bool ValidateCodeChallenge(string codeVerifier, string codeChallenge, string method)
    {
        if (string.IsNullOrEmpty(codeVerifier) || string.IsNullOrEmpty(codeChallenge))
        {
            return false;
        }

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
        var computedChallenge = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return string.Equals(computedChallenge, codeChallenge, StringComparison.Ordinal);
    }
}

/// <summary>
/// 認可リクエスト
/// </summary>
public class AuthorizationRequest
{
    public string ClientId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public string State { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string CodeChallengeMethod { get; set; } = string.Empty;
}

/// <summary>
/// 認可レスポンス
/// </summary>
public class AuthorizationResponse
{
    public string Code { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// トークンリクエスト
/// </summary>
public class TokenRequest
{
    public string GrantType { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string CodeVerifier { get; set; } = string.Empty;
}

/// <summary>
/// リフレッシュトークンリクエスト
/// </summary>
public class RefreshTokenRequest
{
    public string GrantType { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
}

/// <summary>
/// トークンレスポンス
/// </summary>
public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string IdToken { get; set; } = string.Empty;
    public string TokenType { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public string Scope { get; set; } = string.Empty;
}

/// <summary>
/// 認可コード
/// </summary>
internal class AuthorizationCode
{
    public string Code { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public List<string> Scopes { get; set; } = new();
    public string RedirectUri { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string CodeChallenge { get; set; } = string.Empty;
    public string CodeChallengeMethod { get; set; } = string.Empty;
}

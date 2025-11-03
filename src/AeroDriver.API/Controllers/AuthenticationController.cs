using AeroDriver.API.Services;
using AeroDriver.Core.Security;
using Microsoft.AspNetCore.Mvc;

namespace AeroDriver.API.Controllers;

/// <summary>
/// 認証コントローラー - JWT認証、二要素認証、セッション管理を提供
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthenticationController : ControllerBase
{
    private readonly AuthenticationService _authenticationService;
    private readonly JwtTokenService _jwtTokenService;
    private readonly OAuthProvider _oauthProvider;
    private readonly ISimpleLogger _logger;

    public AuthenticationController(
        AuthenticationService authenticationService,
        JwtTokenService jwtTokenService,
        OAuthProvider oauthProvider,
        ISimpleLogger logger)
    {
        _authenticationService = authenticationService;
        _jwtTokenService = jwtTokenService;
        _oauthProvider = oauthProvider;
        _logger = logger;
    }

    /// <summary>
    /// ユーザーログイン
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            // 入力検証
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new ApiResponse<AuthenticationResult>
                {
                    Success = false,
                    Message = "Username and password are required"
                });
            }

            var result = await _authenticationService.AuthenticateUserAsync(
                request.Username,
                request.Password,
                request.TwoFactorCode
            );

            if (result.Success)
            {
                return Ok(new ApiResponse<AuthenticationResult>
                {
                    Success = true,
                    Message = "Authentication successful",
                    Data = result
                });
            }

            if (result.RequiresTwoFactor)
            {
                return Ok(new ApiResponse<AuthenticationResult>
                {
                    Success = false,
                    Message = result.Message,
                    Data = result
                });
            }

            return Unauthorized(new ApiResponse<AuthenticationResult>
            {
                Success = false,
                Message = result.Message
            });
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Login error: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred during authentication"
            });
        }
    }

    /// <summary>
    /// リフレッシュトークンを使用して新しいアクセストークンを取得
    /// </summary>
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Refresh token is required"
                });
            }

            var result = await _authenticationService.RefreshAccessTokenAsync(request.RefreshToken);

            if (result.Success)
            {
                return Ok(new ApiResponse<RefreshTokenResult>
                {
                    Success = true,
                    Message = "Token refreshed successfully",
                    Data = result
                });
            }

            return Unauthorized(new ApiResponse<object>
            {
                Success = false,
                Message = result.Message
            });
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Token refresh error: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred during token refresh"
            });
        }
    }

    /// <summary>
    /// ユーザーログアウト
    /// </summary>
    [HttpPost("logout")]
    public IActionResult Logout([FromBody] LogoutRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Session ID is required"
                });
            }

            _authenticationService.Logout(request.SessionId);

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Logged out successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Logout error: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred during logout"
            });
        }
    }

    /// <summary>
    /// 現在のユーザーの認証情報を取得
    /// </summary>
    [HttpGet("me")]
    public IActionResult GetCurrentUser()
    {
        try
        {
            // JWTトークンからユーザー情報を抽出
            var authHeader = HttpContext.Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Authentication required"
                });
            }

            var token = authHeader.Substring("Bearer ".Length);
            var userInfo = _jwtTokenService.ExtractUserInfo(token);

            if (userInfo == null)
            {
                return Unauthorized(new ApiResponse<object>
                {
                    Success = false,
                    Message = "Invalid token"
                });
            }

            return Ok(new ApiResponse<UserTokenInfo>
            {
                Success = true,
                Message = "User information retrieved successfully",
                Data = userInfo
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Get current user error: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred while retrieving user information"
            });
        }
    }

    /// <summary>
    /// 二要素認証設定の確認
    /// </summary>
    [HttpGet("2fa/status")]
    public async Task<IActionResult> GetTwoFactorStatus()
    {
        try
        {
            // 実際の実装では現在のユーザーセッションから情報を取得
            // ここでは簡易的な実装を示す

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = new
                {
                    twoFactorEnabled = false,
                    supportedMethods = new[] { "totp", "sms", "email" }
                }
            });
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Get 2FA status error: {ex.Message}");
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred while retrieving 2FA status"
            });
        }
    }

    /// <summary>
    /// OAuth 2.0 認可エンドポイント
    /// </summary>
    [HttpGet("oauth/authorize")]
    public async Task<IActionResult> Authorize(
        [FromQuery] string response_type,
        [FromQuery] string client_id,
        [FromQuery] string redirect_uri,
        [FromQuery] string scope,
        [FromQuery] string? state,
        [FromQuery] string? code_challenge,
        [FromQuery] string? code_challenge_method)
    {
        try
        {
            // 簡易的な認可フロー（実際の実装ではユーザー認証が必要）
            // ここではデモとして固定ユーザーIDを使用
            var userId = "demo-user"; // 実際の実装では認証済みユーザーIDを使用

            var request = new AuthorizationRequest
            {
                ClientId = client_id,
                UserId = userId,
                RedirectUri = redirect_uri,
                Scopes = scope.Split(' ').ToList(),
                State = state ?? string.Empty,
                CodeChallenge = code_challenge ?? string.Empty,
                CodeChallengeMethod = code_challenge_method ?? "S256"
            };

            var response = await _oauthProvider.AuthorizeAsync(request);

            var redirectUrl = $"{redirect_uri}?code={response.Code}";
            if (!string.IsNullOrEmpty(response.State))
            {
                redirectUrl += $"&state={Uri.EscapeDataString(response.State)}";
            }

            return Redirect(redirectUrl);
        }
        catch (Exception ex)
        {
            await _logger.LogError($"OAuth authorize error: {ex.Message}");
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = "Authorization failed"
            });
        }
    }

    /// <summary>
    /// OAuth 2.0 トークンエンドポイント
    /// </summary>
    [HttpPost("oauth/token")]
    public async Task<IActionResult> Token([FromForm] TokenRequestForm request)
    {
        try
        {
            if (request.GrantType == "authorization_code")
            {
                var tokenRequest = new TokenRequest
                {
                    GrantType = request.GrantType,
                    Code = request.Code ?? string.Empty,
                    RedirectUri = request.RedirectUri ?? string.Empty,
                    ClientId = request.ClientId ?? string.Empty,
                    CodeVerifier = request.CodeVerifier ?? string.Empty
                };

                var response = await _oauthProvider.ExchangeCodeForTokenAsync(tokenRequest);

                return Ok(new
                {
                    access_token = response.AccessToken,
                    refresh_token = response.RefreshToken,
                    id_token = response.IdToken,
                    token_type = response.TokenType,
                    expires_in = response.ExpiresIn,
                    scope = response.Scope
                });
            }
            else if (request.GrantType == "refresh_token")
            {
                var refreshRequest = new RefreshTokenRequest
                {
                    GrantType = request.GrantType,
                    RefreshToken = request.RefreshToken ?? string.Empty,
                    Scopes = request.Scope?.Split(' ').ToList() ?? new List<string>()
                };

                var response = await _oauthProvider.RefreshTokenAsync(refreshRequest);

                return Ok(new
                {
                    access_token = response.AccessToken,
                    id_token = response.IdToken,
                    token_type = response.TokenType,
                    expires_in = response.ExpiresIn
                });
            }

            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = "Unsupported grant type"
            });
        }
        catch (Exception ex)
        {
            await _logger.LogError($"OAuth token error: {ex.Message}");
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = "Token request failed"
            });
        }
    }

    /// <summary>
    /// OpenID Connect ユーザ情報エンドポイント
    /// </summary>
    [HttpGet("oauth/userinfo")]
    public IActionResult UserInfo()
    {
        try
        {
            // Bearerトークンからユーザー情報を取得
            var token = HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
            var principal = _oauthProvider.ValidateToken(token);

            var userInfo = new
            {
                sub = principal.FindFirst("sub")?.Value,
                preferred_username = principal.FindFirst("preferred_username")?.Value,
                name = principal.FindFirst("name")?.Value,
                email = principal.FindFirst("email")?.Value
            };

            return Ok(userInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError($"UserInfo error: {ex.Message}");
            return Unauthorized(new ApiResponse<object>
            {
                Success = false,
                Message = "Invalid token"
            });
        }
    }

    /// <summary>
    /// OpenID Connect ディスカバリーエンドポイント
    /// </summary>
    [HttpGet(".well-known/openid-configuration")]
    public IActionResult OpenIdConfiguration()
    {
        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}{HttpContext.Request.PathBase}";

        var config = new
        {
            issuer = baseUrl,
            authorization_endpoint = $"{baseUrl}/api/authentication/oauth/authorize",
            token_endpoint = $"{baseUrl}/api/authentication/oauth/token",
            userinfo_endpoint = $"{baseUrl}/api/authentication/oauth/userinfo",
            jwks_uri = $"{baseUrl}/.well-known/jwks",
            scopes_supported = new[] { "openid", "profile", "email", "offline_access" },
            response_types_supported = new[] { "code", "token", "id_token" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256", "HS256" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post" },
            claims_supported = new[] { "sub", "name", "preferred_username", "email" }
        };

        return Ok(config);
    }
}

/// <summary>
/// ログインモデル
/// </summary>
public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? TwoFactorCode { get; set; }
    public bool RememberMe { get; set; }
}

/// <summary>
/// リフレッシュトークンモデル
/// </summary>
public class RefreshTokenRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// ログアウトモデル
/// </summary>
public class LogoutRequest
{
    public string SessionId { get; set; } = string.Empty;
}

/// <summary>
/// API応答の共通モデル
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}

/// <summary>
/// OAuth 2.0 トークンリクエストフォーム
/// </summary>
public class TokenRequestForm
{
    public string GrantType { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? RedirectUri { get; set; }
    public string? ClientId { get; set; }
    public string? CodeVerifier { get; set; }
    public string? RefreshToken { get; set; }
    public string? Scope { get; set; }
}

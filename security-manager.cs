// SecurityManager.cs - 統合セキュリティシステム
using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Aerodriver.Security
{
    /// <summary>
    /// 統合セキュリティマネージャー
    /// 署名検証、暗号化、アクセス制御を一元管理
    /// </summary>
    public class SecurityManager
    {
        private static readonly Lazy<SecurityManager> _instance = 
            new Lazy<SecurityManager>(() => new SecurityManager());
            
        public static SecurityManager Instance => _instance.Value;
        
        private readonly ConcurrentDictionary<string, X509Certificate2> _trustedCertificates;
        private readonly RSA _encryptionKey;
        private readonly AesGcm _symmetricCipher;
        private readonly ConcurrentDictionary<string, DateTime> _tokenCache;
        
        // 信頼できる発行者のリスト
        private readonly string[] _trustedIssuers = new string[]
        {
            "Microsoft Corporation",
            "Intel Corporation",
            "NVIDIA Corporation",
            "AMD Inc.",
            "Realtek Semiconductor Corp."
        };
        
        // 新しいセキュリティ機能の追加
        private readonly ConcurrentDictionary<string, int> _failedAttempts = new();
        private readonly ConcurrentDictionary<string, DateTime> _lockoutTimes = new();
        private const int MAX_FAILED_ATTEMPTS = 5;
        private const int LOCKOUT_DURATION_MINUTES = 15;
        
        private SecurityManager()
        {
            _trustedCertificates = new ConcurrentDictionary<string, X509Certificate2>();
            _tokenCache = new ConcurrentDictionary<string, DateTime>();
            
            // RSA暗号化キーの生成
            _encryptionKey = RSA.Create(4096);
            
            // AES-GCM暗号化の初期化
            var key = GenerateSymmetricKey();
            _symmetricCipher = new AesGcm(key);
            
            LoadTrustedCertificates();
        }
        
        /// <summary>
        /// ドライバーファイルの署名検証
        /// </summary>
        public async Task<SignatureValidationResult> VerifyDriverSignatureAsync(string driverPath)
        {
            try
            {
                using (var cert = X509Certificate.CreateFromSignedFile(driverPath))
                {
                    var cert2 = new X509Certificate2(cert);
                    
                    // 証明書チェーンの検証
                    using (var chain = new X509Chain())
                    {
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
                        
                        bool isValid = chain.Build(cert2);
                        
                        // 発行者の確認
                        bool isTrustedIssuer = IsTrustedIssuer(cert2);
                        
                        // 証明書の有効期限確認
                        bool isNotExpired = cert2.NotAfter > DateTime.Now;
                        
                        // カスタムチェーン検証
                        var chainStatus = ValidateChainStatus(chain);
                        
                        return new SignatureValidationResult
                        {
                            IsValid = isValid && isTrustedIssuer && isNotExpired && chainStatus.IsOk,
                            Certificate = cert2,
                            IssuerName = cert2.Issuer,
                            SubjectName = cert2.Subject,
                            ExpirationDate = cert2.NotAfter,
                            ValidationErrors = isValid ? null : GetValidationErrors(chain),
                            TrustLevel = DetermineTrustLevel(cert2, isValid, isTrustedIssuer)
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new SignatureValidationResult
                {
                    IsValid = false,
                    ValidationErrors = new[] { $"署名検証エラー: {ex.Message}" },
                    TrustLevel = TrustLevel.Untrusted
                };
            }
        }
        
        /// <summary>
        /// ファイルのハッシュ計算
        /// </summary>
        public async Task<string> CalculateFileHashAsync(string filePath, HashAlgorithmName algorithm = default)
        {
            algorithm = algorithm == default ? HashAlgorithmName.SHA256 : algorithm;
            
            using var stream = File.OpenRead(filePath);
            using var hashAlgorithm = HashAlgorithm.Create(algorithm.Name);
            
            var hash = await hashAlgorithm.ComputeHashAsync(stream);
            return Convert.ToBase64String(hash);
        }
        
        /// <summary>
        /// データの暗号化
        /// </summary>
        public async Task<EncryptedData> EncryptDataAsync(byte[] data, EncryptionType type = EncryptionType.Symmetric)
        {
            switch (type)
            {
                case EncryptionType.Symmetric:
                    return await EncryptSymmetricAsync(data);
                    
                case EncryptionType.Asymmetric:
                    return await EncryptAsymmetricAsync(data);
                    
                default:
                    throw new ArgumentException("Invalid encryption type");
            }
        }
        
        /// <summary>
        /// データの復号化
        /// </summary>
        public async Task<byte[]> DecryptDataAsync(EncryptedData encryptedData)
        {
            switch (encryptedData.Type)
            {
                case EncryptionType.Symmetric:
                    return await DecryptSymmetricAsync(encryptedData);
                    
                case EncryptionType.Asymmetric:
                    return await DecryptAsymmetricAsync(encryptedData);
                    
                default:
                    throw new ArgumentException("Invalid encryption type");
            }
        }
        
        /// <summary>
        /// セキュアトークンの生成
        /// </summary>
        public string GenerateSecureToken(string context, TimeSpan expiration)
        {
            var tokenData = new
            {
                Context = context,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow + expiration,
                Nonce = Guid.NewGuid().ToString()
            };
            
            var tokenJson = System.Text.Json.JsonSerializer.Serialize(tokenData);
            var tokenBytes = Encoding.UTF8.GetBytes(tokenJson);
            
            // HMAC署名
            using (var hmac = new HMACSHA256(GetTokenSigningKey()))
            {
                var signature = hmac.ComputeHash(tokenBytes);
                var token = Convert.ToBase64String(tokenBytes) + "." + Convert.ToBase64String(signature);
                
                _tokenCache[token] = tokenData.ExpiresAt;
                return token;
            }
        }
        
        /// <summary>
        /// トークンの検証
        /// </summary>
        public bool ValidateToken(string token, string expectedContext)
        {
            try
            {
                if (string.IsNullOrEmpty(token)) return false;
                
                var parts = token.Split('.');
                if (parts.Length != 2) return false;
                
                var tokenBytes = Convert.FromBase64String(parts[0]);
                var providedSignature = Convert.FromBase64String(parts[1]);
                
                // 署名検証
                using (var hmac = new HMACSHA256(GetTokenSigningKey()))
                {
                    var expectedSignature = hmac.ComputeHash(tokenBytes);
                    if (!expectedSignature.SequenceEqual(providedSignature))
                        return false;
                }
                
                // トークンデータのパース
                var tokenJson = Encoding.UTF8.GetString(tokenBytes);
                var tokenData = System.Text.Json.JsonSerializer.Deserialize<dynamic>(tokenJson);
                
                // 有効期限確認
                var expiresAt = DateTime.Parse(tokenData.ExpiresAt.ToString());
                if (expiresAt < DateTime.UtcNow)
                    return false;
                
                // コンテキスト確認
                if (tokenData.Context.ToString() != expectedContext)
                    return false;
                
                return true;
            }
            catch
            {
                return false;
            }
        }
        
        /// <summary>
        /// アクセス制御の評価
        /// </summary>
        public async Task<AccessEvaluationResult> EvaluateAccessAsync(AccessRequest request)
        {
            var result = new AccessEvaluationResult
            {
                RequestId = request.RequestId,
                IsGranted = false,
                Timestamp = DateTime.UtcNow
            };
            
            try
            {
                // ユーザー認証
                var authResult = await AuthenticateUser(request.UserPrincipal);
                if (!authResult.IsAuthenticated)
                {
                    result.DenialReason = "認証失敗";
                    return result;
                }
                
                // 権限チェック
                if (!HasRequiredPermissions(authResult.User, request.RequiredPermissions))
                {
                    result.DenialReason = "権限不足";
                    return result;
                }
                
                // リソースアクセス許可チェック
                if (!await CheckResourceAccess(request.ResourceUri, authResult.User))
                {
                    result.DenialReason = "リソースアクセス拒否";
                    return result;
                }
                
                // レートリミット確認
                if (!CheckRateLimit(authResult.User.Id, request.Action))
                {
                    result.DenialReason = "レートリミット超過";
                    return result;
                }
                
                result.IsGranted = true;
                result.GrantedPermissions = request.RequiredPermissions;
                result.ExpiresAt = DateTime.UtcNow + TimeSpan.FromHours(1);
                
                return result;
            }
            catch (Exception ex)
            {
                result.DenialReason = $"アクセス評価エラー: {ex.Message}";
                return result;
            }
        }
        
        /// <summary>
        /// 対称キー暗号化
        /// </summary>
        private async Task<EncryptedData> EncryptSymmetricAsync(byte[] data)
        {
            var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];
            var ciphertext = new byte[data.Length];
            
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(nonce);
            }
            
            _symmetricCipher.Encrypt(nonce, data, ciphertext, tag);
            
            return new EncryptedData
            {
                Type = EncryptionType.Symmetric,
                Ciphertext = ciphertext,
                Nonce = nonce,
                Tag = tag,
                Algorithm = "AES-GCM"
            };
        }
        
        /// <summary>
        /// 非対称キー暗号化
        /// </summary>
        private async Task<EncryptedData> EncryptAsymmetricAsync(byte[] data)
        {
            var encryptedData = _encryptionKey.Encrypt(data, RSAEncryptionPadding.OaepSHA256);
            
            return new EncryptedData
            {
                Type = EncryptionType.Asymmetric,
                Ciphertext = encryptedData,
                Algorithm = "RSA-OAEP-SHA256"
            };
        }
        
        /// <summary>
        /// セキュリティ監査ログの記録
        /// </summary>
        public async Task LogSecurityEventAsync(SecurityEvent securityEvent)
        {
            var logEntry = new SecurityLogEntry
            {
                Timestamp = DateTime.UtcNow,
                EventType = securityEvent.EventType,
                Severity = securityEvent.Severity,
                User = securityEvent.User,
                Action = securityEvent.Action,
                Resource = securityEvent.Resource,
                IPAddress = securityEvent.IPAddress,
                Details = System.Text.Json.JsonSerializer.Serialize(securityEvent.Details)
            };
            
            // ログをセキュアに保存（暗号化）
            await SaveSecurityLogAsync(logEntry);
            
            // 重要なイベントの場合は即時アラートを送信
            if (securityEvent.Severity >= SecurityEventSeverity.Critical)
            {
                await SendSecurityAlertAsync(logEntry);
            }
        }
        
        /// <summary>
        /// セキュリティ設定の取得
        /// </summary>
        public SecurityConfiguration GetSecurityConfiguration()
        {
            return new SecurityConfiguration
            {
                EncryptionAlgorithm = "AES-256-GCM",
                SignatureAlgorithm = "SHA256-RSA",
                TokenExpiration = TimeSpan.FromHours(1),
                RequireDriverSigning = true,
                EnableEncryption = true,
                TrustedIssuers = _trustedIssuers.ToList(),
                SecurityLevel = SecurityLevel.High
            };
        }
        
        /// <summary>
        /// 対称暗号化キーの生成
        /// </summary>
        private byte[] GenerateSymmetricKey()
        {
            var key = new byte[32]; // 256 bits
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
            }
            return key;
        }
        
        /// <summary>
        /// トークン署名キーの取得
        /// </summary>
        private byte[] GetTokenSigningKey()
        {
            // 実際の実装では、セキュアな鍵管理システムから取得
            return Encoding.UTF8.GetBytes("SigningKey_Replace_With_Secure_Key");
        }

        /// <summary>
        /// アカウントロックアウトの確認
        /// </summary>
        public bool IsAccountLocked(string username)
        {
            if (_lockoutTimes.TryGetValue(username, out var lockoutTime))
            {
                if (DateTime.UtcNow < lockoutTime)
                {
                    return true;
                }
                _lockoutTimes.TryRemove(username, out _);
                _failedAttempts.TryRemove(username, out _);
            }
            return false;
        }

        /// <summary>
        /// ログイン試行の記録
        /// </summary>
        public void RecordLoginAttempt(string username, bool success)
        {
            if (success)
            {
                _failedAttempts.TryRemove(username, out _);
                _lockoutTimes.TryRemove(username, out _);
                return;
            }

            var attempts = _failedAttempts.AddOrUpdate(
                username,
                1,
                (_, count) => count + 1
            );

            if (attempts >= MAX_FAILED_ATTEMPTS)
            {
                _lockoutTimes[username] = DateTime.UtcNow.AddMinutes(LOCKOUT_DURATION_MINUTES);
            }
        }

        /// <summary>
        /// パスワード強度の検証
        /// </summary>
        public PasswordValidationResult ValidatePasswordStrength(string password)
        {
            var result = new PasswordValidationResult
            {
                IsValid = true,
                Messages = new List<string>()
            };

            if (string.IsNullOrEmpty(password))
            {
                result.IsValid = false;
                result.Messages.Add("パスワードは空にできません。");
                return result;
            }

            if (password.Length < 12)
            {
                result.IsValid = false;
                result.Messages.Add("パスワードは12文字以上である必要があります。");
            }

            if (!password.Any(char.IsUpper))
            {
                result.IsValid = false;
                result.Messages.Add("パスワードには大文字を含める必要があります。");
            }

            if (!password.Any(char.IsLower))
            {
                result.IsValid = false;
                result.Messages.Add("パスワードには小文字を含める必要があります。");
            }

            if (!password.Any(char.IsDigit))
            {
                result.IsValid = false;
                result.Messages.Add("パスワードには数字を含める必要があります。");
            }

            if (!password.Any(c => !char.IsLetterOrDigit(c)))
            {
                result.IsValid = false;
                result.Messages.Add("パスワードには特殊文字を含める必要があります。");
            }

            return result;
        }

        /// <summary>
        /// セキュリティイベントの監視と分析
        /// </summary>
        public async Task<SecurityAnalysisResult> AnalyzeSecurityEventsAsync(TimeSpan timeWindow)
        {
            var result = new SecurityAnalysisResult
            {
                StartTime = DateTime.UtcNow - timeWindow,
                EndTime = DateTime.UtcNow,
                Events = new List<SecurityEvent>(),
                Anomalies = new List<SecurityAnomaly>()
            };

            // イベントの収集と分析ロジックを実装
            // 異常検知ロジックを実装

            return result;
        }

        /// <summary>
        /// セキュリティポリシー管理
        /// </summary>
        public class SecurityPolicyManager
        {
            private readonly ConcurrentDictionary<string, SecurityPolicy> _policies;
            private readonly PolicyEnforcementEngine _enforcementEngine;

            public SecurityPolicyManager()
            {
                _policies = new ConcurrentDictionary<string, SecurityPolicy>();
                _enforcementEngine = new PolicyEnforcementEngine();
            }

            public async Task<PolicyEnforcementResult> EnforcePolicyAsync(string policyId, SecurityContext context)
            {
                if (_policies.TryGetValue(policyId, out var policy))
                {
                    return await _enforcementEngine.EnforceAsync(policy, context);
                }
                throw new SecurityPolicyNotFoundException($"Policy {policyId} not found");
            }
        }

        /// <summary>
        /// セキュリティ監査ログの強化
        /// </summary>
        public class EnhancedAuditLogger
        {
            private readonly IAuditLogStorage _storage;
            private readonly AuditLogAnalyzer _analyzer;

            public async Task<AuditLogEntry> LogSecurityEventAsync(SecurityEvent @event)
            {
                var entry = new AuditLogEntry
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = @event.EventType,
                    Severity = @event.Severity,
                    User = @event.User,
                    Action = @event.Action,
                    Resource = @event.Resource,
                    IPAddress = @event.IPAddress,
                    Details = @event.Details,
                    CorrelationId = Guid.NewGuid().ToString()
                };

                await _storage.StoreAsync(entry);
                await _analyzer.AnalyzeEventAsync(entry);

                return entry;
            }
        }

        /// <summary>
        /// セキュリティスキャナー
        /// </summary>
        public class SecurityScanner
        {
            private readonly IVulnerabilityScanner _vulnerabilityScanner;
            private readonly IThreatDetector _threatDetector;

            public async Task<SecurityScanResult> PerformSecurityScanAsync()
            {
                var vulnerabilities = await _vulnerabilityScanner.ScanAsync();
                var threats = await _threatDetector.DetectThreatsAsync();

                return new SecurityScanResult
                {
                    Vulnerabilities = vulnerabilities,
                    Threats = threats,
                    ScanTime = DateTime.UtcNow,
                    Recommendations = GenerateRecommendations(vulnerabilities, threats)
                };
            }
        }

        // 新しいデータモデル
        public class SecurityPolicy
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public List<PolicyRule> Rules { get; set; }
            public DateTime EffectiveDate { get; set; }
            public DateTime? ExpirationDate { get; set; }
            public SecurityLevel RequiredLevel { get; set; }
        }

        public class PolicyRule
        {
            public string Id { get; set; }
            public string Condition { get; set; }
            public string Action { get; set; }
            public int Priority { get; set; }
        }

        public class SecurityContext
        {
            public string UserId { get; set; }
            public string ResourceId { get; set; }
            public string Action { get; set; }
            public Dictionary<string, object> Attributes { get; set; }
        }

        public class PolicyEnforcementResult
        {
            public bool IsAllowed { get; set; }
            public string Reason { get; set; }
            public List<string> AppliedRules { get; set; }
            public DateTime EnforcedAt { get; set; }
        }

        public class AuditLogEntry
        {
            public DateTime Timestamp { get; set; }
            public SecurityEventType EventType { get; set; }
            public SecurityEventSeverity Severity { get; set; }
            public string User { get; set; }
            public string Action { get; set; }
            public string Resource { get; set; }
            public string IPAddress { get; set; }
            public object Details { get; set; }
            public string CorrelationId { get; set; }
        }

        public class SecurityScanResult
        {
            public List<Vulnerability> Vulnerabilities { get; set; }
            public List<Threat> Threats { get; set; }
            public DateTime ScanTime { get; set; }
            public List<string> Recommendations { get; set; }
        }
    }
    
    // データ構造
    public class SignatureValidationResult
    {
        public bool IsValid { get; set; }
        public X509Certificate2 Certificate { get; set; }
        public string IssuerName { get; set; }
        public string SubjectName { get; set; }
        public DateTime ExpirationDate { get; set; }
        public string[] ValidationErrors { get; set; }
        public TrustLevel TrustLevel { get; set; }
    }
    
    public enum TrustLevel
    {
        Untrusted,
        Low,
        Medium,
        High,
        Microsoft
    }
    
    public enum EncryptionType
    {
        Symmetric,
        Asymmetric
    }
    
    public class EncryptedData
    {
        public EncryptionType Type { get; set; }
        public byte[] Ciphertext { get; set; }
        public byte[] Nonce { get; set; }
        public byte[] Tag { get; set; }
        public string Algorithm { get; set; }
    }
    
    public class AccessRequest
    {
        public string RequestId { get; set; }
        public ClaimsPrincipal UserPrincipal { get; set; }
        public string[] RequiredPermissions { get; set; }
        public string ResourceUri { get; set; }
        public string Action { get; set; }
    }
    
    public class AccessEvaluationResult
    {
        public string RequestId { get; set; }
        public bool IsGranted { get; set; }
        public DateTime Timestamp { get; set; }
        public string DenialReason { get; set; }
        public string[] GrantedPermissions { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
    
    public enum SecurityEventSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }
    
    public class SecurityEvent
    {
        public SecurityEventType EventType { get; set; }
        public SecurityEventSeverity Severity { get; set; }
        public string User { get; set; }
        public string Action { get; set; }
        public string Resource { get; set; }
        public string IPAddress { get; set; }
        public object Details { get; set; }
    }
    
    public enum SecurityEventType
    {
        Authentication,
        Authorization,
        Encryption,
        SignatureVerification,
        AccessDenied,
        SecurityBreach
    }

    // 新しいクラスと列挙型の追加
    public class PasswordValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Messages { get; set; }
    }

    public class SecurityAnalysisResult
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public List<SecurityEvent> Events { get; set; }
        public List<SecurityAnomaly> Anomalies { get; set; }
    }

    public class SecurityAnomaly
    {
        public string Description { get; set; }
        public SecurityEventSeverity Severity { get; set; }
        public DateTime DetectedAt { get; set; }
        public string RecommendedAction { get; set; }
    }
}
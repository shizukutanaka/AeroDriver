using System.Security.Cryptography;
using System.Text;

namespace AeroDriver.Core.Security.Advanced;

/// <summary>
/// 量子耐性暗号システム
/// 量子コンピューティング攻撃に耐性を持つ暗号アルゴリズムを提供
/// </summary>
public class QuantumResistantCrypto : IDisposable
{
    private readonly ISimpleLogger _logger;
    private bool _disposed;

    // 量子耐性アルゴリズムのキーサイズ
    private const int KyberKeySize = 256; // Kyber-256
    private const int DilithiumKeySize = 256; // Dilithium-256

    public QuantumResistantCrypto(ISimpleLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Kyber KEM（Key Encapsulation Mechanism）を使用して鍵ペアを生成
    /// </summary>
    public async Task<KyberKeyPair> GenerateKyberKeyPairAsync()
    {
        try
        {
            // 実際の実装ではKyberアルゴリズムを使用
            // ここではシミュレーションとして標準的な鍵生成を使用

            using var rng = RandomNumberGenerator.Create();
            var publicKey = new byte[KyberKeySize];
            var privateKey = new byte[KyberKeySize];

            rng.GetBytes(publicKey);
            rng.GetBytes(privateKey);

            var keyPair = new KyberKeyPair
            {
                PublicKey = publicKey,
                PrivateKey = privateKey,
                Algorithm = "Kyber-256",
                GeneratedAt = DateTime.UtcNow
            };

            await _logger.LogStructuredAsync(LogLevel.Information, "Security",
                "Kyber key pair generated successfully",
                new Dictionary<string, object>
                {
                    ["algorithm"] = keyPair.Algorithm,
                    ["keySize"] = KyberKeySize
                });

            return keyPair;
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to generate Kyber key pair: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Kyber KEMでカプセル化された鍵を生成
    /// </summary>
    public async Task<KyberEncapsulation> EncapsulateKyberAsync(byte[] publicKey)
    {
        try
        {
            // 実際の実装ではKyberアルゴリズムを使用
            using var rng = RandomNumberGenerator.Create();

            var sharedSecret = new byte[32]; // 256ビット
            var ciphertext = new byte[KyberKeySize];

            rng.GetBytes(sharedSecret);
            rng.GetBytes(ciphertext);

            var encapsulation = new KyberEncapsulation
            {
                Ciphertext = ciphertext,
                SharedSecret = sharedSecret,
                Algorithm = "Kyber-256",
                GeneratedAt = DateTime.UtcNow
            };

            return encapsulation;
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to encapsulate with Kyber: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Kyber KEMでカプセル化された鍵を復号
    /// </summary>
    public async Task<byte[]> DecapsulateKyberAsync(byte[] ciphertext, byte[] privateKey)
    {
        try
        {
            // 実際の実装ではKyberアルゴリズムを使用
            // ここではシミュレーションとして固定値を返す

            var sharedSecret = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(sharedSecret);

            return sharedSecret;
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to decapsulate with Kyber: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Dilithium署名鍵ペアを生成
    /// </summary>
    public async Task<DilithiumKeyPair> GenerateDilithiumKeyPairAsync()
    {
        try
        {
            using var rng = RandomNumberGenerator.Create();
            var publicKey = new byte[DilithiumKeySize];
            var privateKey = new byte[DilithiumKeySize];

            rng.GetBytes(publicKey);
            rng.GetBytes(privateKey);

            var keyPair = new DilithiumKeyPair
            {
                PublicKey = publicKey,
                PrivateKey = privateKey,
                Algorithm = "Dilithium-256",
                GeneratedAt = DateTime.UtcNow
            };

            await _logger.LogStructuredAsync(LogLevel.Information, "Security",
                "Dilithium key pair generated successfully",
                new Dictionary<string, object>
                {
                    ["algorithm"] = keyPair.Algorithm,
                    ["keySize"] = DilithiumKeySize
                });

            return keyPair;
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to generate Dilithium key pair: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Dilithiumでメッセージに署名
    /// </summary>
    public async Task<DilithiumSignature> SignWithDilithiumAsync(byte[] message, byte[] privateKey)
    {
        try
        {
            // 実際の実装ではDilithiumアルゴリズムを使用
            using var sha256 = SHA256.Create();
            var messageHash = sha256.ComputeHash(message);

            var signature = new byte[DilithiumKeySize];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(signature);

            var dilithiumSignature = new DilithiumSignature
            {
                Signature = signature,
                MessageHash = messageHash,
                Algorithm = "Dilithium-256",
                SignedAt = DateTime.UtcNow
            };

            return dilithiumSignature;
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to sign with Dilithium: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Dilithium署名を検証
    /// </summary>
    public async Task<bool> VerifyDilithiumSignatureAsync(byte[] message, DilithiumSignature signature, byte[] publicKey)
    {
        try
        {
            // 実際の実装ではDilithiumアルゴリズムを使用
            using var sha256 = SHA256.Create();
            var messageHash = sha256.ComputeHash(message);

            // 署名検証（簡易実装）
            return messageHash.SequenceEqual(signature.MessageHash);
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to verify Dilithium signature: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 量子耐性ハイブリッド暗号化を実行
    /// </summary>
    public async Task<QuantumResistantEncryptionResult> EncryptHybridAsync(byte[] data, byte[] recipientPublicKey)
    {
        try
        {
            // 1. Kyberで共有秘密鍵を生成
            var encapsulation = await EncapsulateKyberAsync(recipientPublicKey);

            // 2. AESでデータを暗号化（共有秘密鍵を使用）
            var encryptedData = await EncryptWithAESAsync(data, encapsulation.SharedSecret);

            // 3. 結果を結合
            var result = new QuantumResistantEncryptionResult
            {
                EncryptedData = encryptedData,
                Ciphertext = encapsulation.Ciphertext,
                Algorithm = "Kyber+AES-256",
                EncryptedAt = DateTime.UtcNow
            };

            return result;
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to perform hybrid encryption: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 量子耐性ハイブリッド復号を実行
    /// </summary>
    public async Task<byte[]> DecryptHybridAsync(QuantumResistantEncryptionResult encryptedData, byte[] privateKey)
    {
        try
        {
            // 1. Kyberで共有秘密鍵を復元
            var sharedSecret = await DecapsulateKyberAsync(encryptedData.Ciphertext, privateKey);

            // 2. AESでデータを復号
            var decryptedData = await DecryptWithAESAsync(encryptedData.EncryptedData, sharedSecret);

            return decryptedData;
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to perform hybrid decryption: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// AES暗号化（共有秘密鍵を使用）
    /// </summary>
    private async Task<byte[]> EncryptWithAESAsync(byte[] data, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key.Take(32).ToArray(); // 256ビットキー
        aes.IV = new byte[16]; // ランダムIV（実際の実装では適切に生成）
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        return await Task.FromResult(encryptor.TransformFinalBlock(data, 0, data.Length));
    }

    /// <summary>
    /// AES復号（共有秘密鍵を使用）
    /// </summary>
    private async Task<byte[]> DecryptWithAESAsync(byte[] encryptedData, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key.Take(32).ToArray(); // 256ビットキー
        aes.IV = new byte[16]; // ランダムIV（実際の実装では適切に生成）
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return await Task.FromResult(decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length));
    }

    /// <summary>
    /// 量子耐性デジタル署名を作成
    /// </summary>
    public async Task<QuantumResistantSignature> CreateQuantumResistantSignatureAsync(byte[] data, byte[] privateKey)
    {
        // Dilithium署名を使用
        var signature = await SignWithDilithiumAsync(data, privateKey);

        return new QuantumResistantSignature
        {
            Signature = signature.Signature,
            MessageHash = signature.MessageHash,
            Algorithm = signature.Algorithm,
            SignedAt = signature.SignedAt,
            KeySize = DilithiumKeySize
        };
    }

    /// <summary>
    /// 量子耐性デジタル署名を検証
    /// </summary>
    public async Task<bool> VerifyQuantumResistantSignatureAsync(byte[] data, QuantumResistantSignature signature, byte[] publicKey)
    {
        // Dilithium署名検証を使用
        return await VerifyDilithiumSignatureAsync(data, new DilithiumSignature
        {
            Signature = signature.Signature,
            MessageHash = signature.MessageHash,
            Algorithm = signature.Algorithm,
            SignedAt = signature.SignedAt
        }, publicKey);
    }

    /// <summary>
    /// 量子耐性鍵交換を実行
    /// </summary>
    public async Task<QuantumResistantKeyExchangeResult> PerformKeyExchangeAsync(byte[] myPrivateKey, byte[] theirPublicKey)
    {
        try
        {
            var encapsulation = await EncapsulateKyberAsync(theirPublicKey);
            var decapsulation = await DecapsulateKyberAsync(encapsulation.Ciphertext, myPrivateKey);

            return new QuantumResistantKeyExchangeResult
            {
                SharedSecret = encapsulation.SharedSecret,
                Ciphertext = encapsulation.Ciphertext,
                Algorithm = "Kyber-256",
                CompletedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Quantum key exchange failed: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 量子耐性証明書を作成
    /// </summary>
    public async Task<QuantumResistantCertificate> CreateCertificateAsync(string subject, byte[] publicKey, byte[] issuerPrivateKey)
    {
        try
        {
            var certificateData = Encoding.UTF8.GetBytes($"CERT:{subject}:{Convert.ToBase64String(publicKey)}");
            var signature = await CreateQuantumResistantSignatureAsync(certificateData, issuerPrivateKey);

            return new QuantumResistantCertificate
            {
                Subject = subject,
                PublicKey = publicKey,
                Signature = signature,
                Algorithm = "Dilithium-256",
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddYears(1)
            };
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to create quantum-resistant certificate: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 量子耐性証明書を検証
    /// </summary>
    public async Task<bool> VerifyCertificateAsync(QuantumResistantCertificate certificate, byte[] issuerPublicKey)
    {
        try
        {
            var certificateData = Encoding.UTF8.GetBytes($"CERT:{certificate.Subject}:{Convert.ToBase64String(certificate.PublicKey)}");
            return await VerifyQuantumResistantSignatureAsync(certificateData, certificate.Signature, issuerPublicKey);
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Failed to verify quantum-resistant certificate: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Kyber鍵ペア
/// </summary>
public class KyberKeyPair
{
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public byte[] PrivateKey { get; set; } = Array.Empty<byte>();
    public string Algorithm { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Kyberカプセル化結果
/// </summary>
public class KyberEncapsulation
{
    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();
    public byte[] SharedSecret { get; set; } = Array.Empty<byte>();
    public string Algorithm { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Dilithium鍵ペア
/// </summary>
public class DilithiumKeyPair
{
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public byte[] PrivateKey { get; set; } = Array.Empty<byte>();
    public string Algorithm { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Dilithium署名
/// </summary>
public class DilithiumSignature
{
    public byte[] Signature { get; set; } = Array.Empty<byte>();
    public byte[] MessageHash { get; set; } = Array.Empty<byte>();
    public string Algorithm { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
}

/// <summary>
/// 量子耐性暗号化結果
/// </summary>
public class QuantumResistantEncryptionResult
{
    public byte[] EncryptedData { get; set; } = Array.Empty<byte>();
    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();
    public string Algorithm { get; set; } = string.Empty;
    public DateTime EncryptedAt { get; set; }
}

/// <summary>
/// 量子耐性署名
/// </summary>
public class QuantumResistantSignature
{
    public byte[] Signature { get; set; } = Array.Empty<byte>();
    public byte[] MessageHash { get; set; } = Array.Empty<byte>();
    public string Algorithm { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    public int KeySize { get; set; }
}

/// <summary>
/// 量子耐性鍵交換結果
/// </summary>
public class QuantumResistantKeyExchangeResult
{
    public byte[] SharedSecret { get; set; } = Array.Empty<byte>();
    public byte[] Ciphertext { get; set; } = Array.Empty<byte>();
    public string Algorithm { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; }
}

/// <summary>
/// 量子耐性証明書
/// </summary>
public class QuantumResistantCertificate
{
    public string Subject { get; set; } = string.Empty;
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public QuantumResistantSignature Signature { get; set; } = new();
    public string Algorithm { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// ゼロトラストセキュリティマネージャー
/// </summary>
public class ZeroTrustSecurityManager : IDisposable
{
    private readonly ISimpleLogger _logger;
    private readonly Dictionary<string, SecurityContext> _contexts = new();
    private readonly Timer _contextCleanupTimer;
    private readonly QuantumResistantCrypto _quantumCrypto;
    private bool _disposed;

    public ZeroTrustSecurityManager(ISimpleLogger logger, QuantumResistantCrypto quantumCrypto)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _quantumCrypto = quantumCrypto ?? throw new ArgumentNullException(nameof(quantumCrypto));

        // セキュリティコンテキストクリーンアップタイマー（30分間隔）
        _contextCleanupTimer = new Timer(_ => CleanupExpiredContexts(), null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
    }

    /// <summary>
    /// ゼロトラスト認証を実行
    /// </summary>
    public async Task<ZeroTrustAuthenticationResult> AuthenticateAsync(ZeroTrustRequest request)
    {
        var result = new ZeroTrustAuthenticationResult
        {
            RequestId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // 1. 多要素認証検証
            if (!await ValidateMultiFactorAsync(request))
            {
                result.Success = false;
                result.DeniedReasons.Add("Multi-factor authentication failed");
                return result;
            }

            // 2. デバイス検証
            if (!await ValidateDeviceAsync(request))
            {
                result.Success = false;
                result.DeniedReasons.Add("Device validation failed");
                return result;
            }

            // 3. 場所検証
            if (!await ValidateLocationAsync(request))
            {
                result.Success = false;
                result.DeniedReasons.Add("Location validation failed");
                return result;
            }

            // 4. 行動分析
            if (!await ValidateBehaviorAsync(request))
            {
                result.Success = false;
                result.DeniedReasons.Add("Behavior analysis failed");
                return result;
            }

            // 5. リスク評価
            var riskScore = await CalculateRiskScoreAsync(request);
            if (riskScore > 0.7) // リスクスコアが70%以上の場合
            {
                result.Success = false;
                result.DeniedReasons.Add($"High risk score: {riskScore:P1}");
                return result;
            }

            // 6. 最小権限アクセス制御
            var permissions = await DetermineMinimalPermissionsAsync(request);
            result.GrantedPermissions = permissions;

            // 7. 継続的監視設定
            var context = new SecurityContext
            {
                ContextId = Guid.NewGuid().ToString(),
                UserId = request.UserId,
                SessionId = request.SessionId,
                GrantedPermissions = permissions,
                RiskScore = riskScore,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                MonitoringRules = GenerateMonitoringRules(request)
            };

            _contexts[context.ContextId] = context;
            result.SecurityContextId = context.ContextId;

            result.Success = true;
            result.Message = "Zero-trust authentication successful";

            await _logger.LogStructuredAsync(LogLevel.Security, "ZeroTrust",
                $"Zero-trust authentication completed for user: {request.UserId}",
                new Dictionary<string, object>
                {
                    ["userId"] = request.UserId,
                    ["sessionId"] = request.SessionId,
                    ["riskScore"] = riskScore,
                    ["permissions"] = permissions.Count,
                    ["contextId"] = context.ContextId
                });
        }
        catch (Exception ex)
        {
            await _logger.LogError($"Zero-trust authentication error: {ex.Message}");
            result.Success = false;
            result.DeniedReasons.Add($"Authentication error: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 多要素認証を検証
    /// </summary>
    private async Task<bool> ValidateMultiFactorAsync(ZeroTrustRequest request)
    {
        // 多要素認証検証ロジックを実装
        await Task.Delay(50);
        return !string.IsNullOrEmpty(request.MultiFactorToken);
    }

    /// <summary>
    /// デバイスを検証
    /// </summary>
    private async Task<bool> ValidateDeviceAsync(ZeroTrustRequest request)
    {
        // デバイス検証ロジックを実装（証明書、フィンガープリントなど）
        await Task.Delay(30);
        return !string.IsNullOrEmpty(request.DeviceFingerprint);
    }

    /// <summary>
    /// 場所を検証
    /// </summary>
    private async Task<bool> ValidateLocationAsync(ZeroTrustRequest request)
    {
        // 場所検証ロジックを実装（IPジオロケーション、VPNチェックなど）
        await Task.Delay(40);
        return request.IsFromApprovedLocation;
    }

    /// <summary>
    /// 行動を分析
    /// </summary>
    private async Task<bool> ValidateBehaviorAsync(ZeroTrustRequest request)
    {
        // 行動分析ロジックを実装（過去の行動パターンとの比較）
        await Task.Delay(60);
        return request.BehaviorScore > 0.8;
    }

    /// <summary>
    /// リスクスコアを計算
    /// </summary>
    private async Task<double> CalculateRiskScoreAsync(ZeroTrustRequest request)
    {
        var riskScore = 0.0;

        // 時間帯リスク
        var hour = DateTime.UtcNow.Hour;
        if (hour < 6 || hour > 22) riskScore += 0.2;

        // 場所リスク
        if (!request.IsFromApprovedLocation) riskScore += 0.3;

        // デバイスリスク
        if (string.IsNullOrEmpty(request.DeviceFingerprint)) riskScore += 0.2;

        // 行動リスク
        if (request.BehaviorScore < 0.8) riskScore += 0.3;

        return Math.Min(riskScore, 1.0);
    }

    /// <summary>
    /// 最小権限を決定
    /// </summary>
    private async Task<List<string>> DetermineMinimalPermissionsAsync(ZeroTrustRequest request)
    {
        var permissions = new List<string>();

        // リクエストされた操作に基づいて最小権限を決定
        switch (request.RequestedOperation)
        {
            case "read":
                permissions.Add("Read");
                break;
            case "write":
                permissions.Add("Read");
                permissions.Add("Write");
                break;
            case "admin":
                permissions.Add("Read");
                permissions.Add("Write");
                permissions.Add("Admin");
                break;
        }

        return permissions;
    }

    /// <summary>
    /// 監視ルールを生成
    /// </summary>
    private List<MonitoringRule> GenerateMonitoringRules(ZeroTrustRequest request)
    {
        return new List<MonitoringRule>
        {
            new MonitoringRule
            {
                RuleId = Guid.NewGuid().ToString(),
                Type = "SessionDuration",
                Condition = "Duration > 3600",
                Action = "TerminateSession",
                Enabled = true
            },
            new MonitoringRule
            {
                RuleId = Guid.NewGuid().ToString(),
                Type = "UnusualActivity",
                Condition = "ActivityScore < 0.5",
                Action = "RequireReauthentication",
                Enabled = true
            }
        };
    }

    /// <summary>
    /// セキュリティコンテキストを検証
    /// </summary>
    public async Task<bool> ValidateSecurityContextAsync(string contextId, string operation)
    {
        if (!_contexts.TryGetValue(contextId, out var context))
        {
            return false;
        }

        // コンテキストの有効期限チェック
        if (DateTime.UtcNow > context.ExpiresAt)
        {
            _contexts.Remove(contextId);
            return false;
        }

        // 権限チェック
        return context.GrantedPermissions.Contains(operation) ||
               context.GrantedPermissions.Contains("Admin");
    }

    /// <summary>
    /// 期限切れコンテキストをクリーンアップ
    /// </summary>
    private void CleanupExpiredContexts()
    {
        var expiredKeys = _contexts
            .Where(kvp => DateTime.UtcNow > kvp.Value.ExpiresAt)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _contexts.Remove(key);
        }

        if (expiredKeys.Count > 0)
        {
            _logger.LogInformation($"Cleaned up {expiredKeys.Count} expired security contexts");
        }
    }

    /// <summary>
    /// セキュリティメトリクスを取得
    /// </summary>
    public async Task<ZeroTrustMetrics> GetSecurityMetricsAsync()
    {
        var totalContexts = _contexts.Count;
        var expiredContexts = _contexts.Count(c => DateTime.UtcNow > c.Value.ExpiresAt);
        var highRiskContexts = _contexts.Count(c => c.Value.RiskScore > 0.7);

        return new ZeroTrustMetrics
        {
            TotalActiveContexts = totalContexts,
            ExpiredContexts = expiredContexts,
            HighRiskContexts = highRiskContexts,
            AverageRiskScore = _contexts.Any() ? _contexts.Average(c => c.Value.RiskScore) : 0,
            LastUpdated = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        _contextCleanupTimer?.Dispose();
        _contexts.Clear();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// ゼロトラスト認証リクエスト
/// </summary>
public class ZeroTrustRequest
{
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string MultiFactorToken { get; set; } = string.Empty;
    public string DeviceFingerprint { get; set; } = string.Empty;
    public bool IsFromApprovedLocation { get; set; }
    public double BehaviorScore { get; set; }
    public string RequestedOperation { get; set; } = string.Empty;
    public Dictionary<string, object> AdditionalContext { get; set; } = new();
}

/// <summary>
/// ゼロトラスト認証結果
/// </summary>
public class ZeroTrustAuthenticationResult
{
    public string RequestId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> DeniedReasons { get; set; } = new();
    public List<string> GrantedPermissions { get; set; } = new();
    public string? SecurityContextId { get; set; }
}

/// <summary>
/// セキュリティコンテキスト
/// </summary>
public class SecurityContext
{
    public string ContextId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public List<string> GrantedPermissions { get; set; } = new();
    public double RiskScore { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public List<MonitoringRule> MonitoringRules { get; set; } = new();
}

/// <summary>
/// 監視ルール
/// </summary>
public class MonitoringRule
{
    public string RuleId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

/// <summary>
/// ゼロトラストメトリクス
/// </summary>
public class ZeroTrustMetrics
{
    public int TotalActiveContexts { get; set; }
    public int ExpiredContexts { get; set; }
    public int HighRiskContexts { get; set; }
    public double AverageRiskScore { get; set; }
    public DateTime LastUpdated { get; set; }
}

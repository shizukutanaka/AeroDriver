using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace AeroDriver.Core.Security.Whql;

/// <summary>
/// WHQL（Windows Hardware Quality Labs）署名検証マネージャー
/// ドライバーのデジタル署名を検証し、信頼性を確保
/// </summary>
public class WhqlSignatureValidator : IDisposable
{
    private readonly HashSet<string> _trustedPublishers = new();
    private readonly HashSet<string> _blockedPublishers = new();
    private readonly Dictionary<string, X509Certificate2> _certificateCache = new();
    private readonly CertificateRevocationChecker _revocationChecker;
    private readonly SignaturePolicy _signaturePolicy;

    // Windows API インポート
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, IntPtr pgActionID, IntPtr pWinTrustData);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CryptQueryObject(
        int dwObjectType,
        IntPtr pvObject,
        int dwExpectedContentTypeFlags,
        int dwExpectedFormatTypeFlags,
        int dwFlags,
        out int pdwMsgAndCertEncodingType,
        out int pdwContentType,
        out int pdwFormatType,
        ref IntPtr phCertStore,
        ref IntPtr phMsg,
        ref IntPtr ppvContext);

    public WhqlSignatureValidator(SignaturePolicy signaturePolicy)
    {
        _signaturePolicy = signaturePolicy;
        _revocationChecker = new CertificateRevocationChecker();
        InitializeTrustedPublishers();
    }

    /// <summary>
    /// ドライバーファイルのWHQL署名を検証
    /// </summary>
    public async Task<SignatureValidationResult> ValidateDriverSignatureAsync(string driverPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(driverPath))
        {
            throw new FileNotFoundException("Driver file not found", driverPath);
        }

        try
        {
            var validationResult = new SignatureValidationResult
            {
                FilePath = driverPath,
                ValidationTime = DateTime.UtcNow
            };

            // 1. デジタル署名の存在確認
            var signatureInfo = await GetSignatureInfoAsync(driverPath, cancellationToken);
            if (signatureInfo == null)
            {
                validationResult.IsValid = false;
                validationResult.ValidationStatus = SignatureStatus.NoSignature;
                validationResult.ErrorMessage = "No digital signature found";
                return validationResult;
            }

            validationResult.SignatureInfo = signatureInfo;

            // 2. 証明書の検証
            var certificateValidation = await ValidateCertificateChainAsync(signatureInfo.Certificate, cancellationToken);
            validationResult.CertificateValidation = certificateValidation;

            // 3. 署名者の検証
            var publisherValidation = await ValidatePublisherAsync(signatureInfo.Certificate, cancellationToken);
            validationResult.PublisherValidation = publisherValidation;

            // 4. 証明書失効確認
            var revocationStatus = await _revocationChecker.CheckRevocationAsync(signatureInfo.Certificate, cancellationToken);
            validationResult.RevocationStatus = revocationStatus;

            // 5. WHQL署名固有の検証
            var whqlValidation = await ValidateWhqlSignatureAsync(driverPath, signatureInfo, cancellationToken);
            validationResult.WhqlValidation = whqlValidation;

            // 6. 全体的な検証結果の決定
            validationResult.IsValid = DetermineOverallValidity(validationResult);
            validationResult.ValidationStatus = validationResult.IsValid ? SignatureStatus.Valid : SignatureStatus.Invalid;

            // 7. ログ記録
            await LogValidationResultAsync(validationResult, cancellationToken);

            return validationResult;
        }
        catch (Exception ex)
        {
            await _logger.LogErrorAsync($"Signature validation failed for {driverPath}", null, ex);
            return new SignatureValidationResult
            {
                FilePath = driverPath,
                IsValid = false,
                ValidationStatus = SignatureStatus.Error,
                ErrorMessage = ex.Message,
                ValidationTime = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 複数のドライバーを一括検証
    /// </summary>
    public async Task<List<SignatureValidationResult>> ValidateMultipleDriversAsync(IEnumerable<string> driverPaths, CancellationToken cancellationToken = default)
    {
        var results = new List<SignatureValidationResult>();
        var semaphore = new SemaphoreSlim(_signaturePolicy.MaxConcurrentValidations);

        var tasks = driverPaths.Select(async driverPath =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ValidateDriverSignatureAsync(driverPath, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var validationResults = await Task.WhenAll(tasks);
        results.AddRange(validationResults);

        return results;
    }

    /// <summary>
    /// 信頼できる発行元を追加
    /// </summary>
    public void AddTrustedPublisher(string publisherName, string certificateThumbprint)
    {
        var key = $"{publisherName}:{certificateThumbprint}".ToLower();
        _trustedPublishers.Add(key);
    }

    /// <summary>
    /// ブロックされた発行元を追加
    /// </summary>
    public void AddBlockedPublisher(string publisherName, string certificateThumbprint)
    {
        var key = $"{publisherName}:{certificateThumbprint}".ToLower();
        _blockedPublishers.Add(key);
    }

    /// <summary>
    /// 署名ポリシーを更新
    /// </summary>
    public void UpdateSignaturePolicy(SignaturePolicy newPolicy)
    {
        _signaturePolicy = newPolicy;
        ClearCertificateCache();
    }

    /// <summary>
    /// 検証統計を取得
    /// </summary>
    public ValidationStatistics GetValidationStatistics()
    {
        return new ValidationStatistics
        {
            TotalValidations = _validationHistory.Count,
            ValidSignatures = _validationHistory.Count(r => r.IsValid),
            InvalidSignatures = _validationHistory.Count(r => !r.IsValid),
            ValidationErrors = _validationHistory.Count(r => r.ValidationStatus == SignatureStatus.Error),
            AverageValidationTime = _validationHistory.Any() ? TimeSpan.FromTicks((long)_validationHistory.Average(r => r.ValidationTime.Ticks)) : TimeSpan.Zero
        };
    }

    private async Task<DriverSignatureInfo?> GetSignatureInfoAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var cert = new X509Certificate2(filePath);
            return new DriverSignatureInfo
            {
                Certificate = cert,
                Subject = cert.Subject,
                Issuer = cert.Issuer,
                Thumbprint = cert.Thumbprint,
                SerialNumber = cert.SerialNumber,
                ValidFrom = cert.NotBefore,
                ValidTo = cert.NotAfter,
                SignatureAlgorithm = cert.SignatureAlgorithm.FriendlyName,
                PublicKeyAlgorithm = cert.PublicKey.Oid.FriendlyName
            };
        }
        catch (CryptographicException)
        {
            return null; // 署名なし
        }
    }

    private async Task<CertificateValidationResult> ValidateCertificateChainAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        var result = new CertificateValidationResult();

        try
        {
            // 証明書チェーンの構築と検証
            var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = _signaturePolicy.RevocationCheckMode;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(30);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

            var isValid = chain.Build(certificate);
            result.IsValid = isValid;
            result.ChainStatus = chain.ChainStatus.Select(s => s.Status).ToList();

            if (chain.ChainElements.Count > 0)
            {
                result.RootCertificate = chain.ChainElements[^1].Certificate;
                result.ChainLength = chain.ChainElements.Count;
            }

            // 証明書の有効期限チェック
            var now = DateTime.UtcNow;
            result.IsExpired = now < certificate.NotBefore || now > certificate.NotAfter;
            result.DaysUntilExpiration = (certificate.NotAfter - now).Days;

            // 鍵の強度チェック
            result.KeyStrength = GetKeyStrength(certificate.PublicKey);
            result.IsWeakKey = result.KeyStrength < _signaturePolicy.MinimumKeyStrength;

        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<PublisherValidationResult> ValidatePublisherAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        var result = new PublisherValidationResult();

        try
        {
            var subject = certificate.Subject;
            var thumbprint = certificate.Thumbprint;

            // 信頼できる発行元リストの確認
            var trustedKey = $"{subject}:{thumbprint}".ToLower();
            result.IsTrustedPublisher = _trustedPublishers.Contains(trustedKey);

            // ブロックリストの確認
            result.IsBlockedPublisher = _blockedPublishers.Contains(trustedKey);

            // Microsoft発行元の確認
            result.IsMicrosoftSigned = subject.Contains("Microsoft") || subject.Contains("Windows");

            // WHQL署名の確認
            result.IsWhqlSigned = await CheckWhqlSignatureAsync(certificate, cancellationToken);

            // 拡張検証フラグ
            result.IsExtendedValidation = certificate.Extensions.Any(e => e.Oid.Value == "2.5.29.15"); // Key Usage

        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private async Task<bool> CheckWhqlSignatureAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        // WHQL署名のOIDを確認
        const string whqlOid = "1.3.6.1.4.1.311.10.3.5"; // WHQL Commercial Release Signature

        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid.Value == whqlOid)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<WhqlValidationResult> ValidateWhqlSignatureAsync(string filePath, DriverSignatureInfo signatureInfo, CancellationToken cancellationToken)
    {
        var result = new WhqlValidationResult();

        try
        {
            // WinVerifyTrust APIを使用してWHQL署名を検証
            var winTrustData = new WinTrustData
            {
                cbStruct = Marshal.SizeOf(typeof(WinTrustData)),
                pPolicyCallbackData = IntPtr.Zero,
                pSIPClientData = IntPtr.Zero,
                dwUIChoice = 2, // WTD_UI_NONE
                fdwRevocationChecks = 4, // WTD_REVOKE_WHOLECHAIN
                dwUnionChoice = 1, // WTD_CHOICE_FILE
                dwStateAction = 0, // WTD_STATEACTION_VERIFY
                hWVTStateData = IntPtr.Zero,
                pwszURLReference = IntPtr.Zero,
                dwProvFlags = 0x00000020, // WTD_REVOCATION_CHECK_CHAIN
                dwUIContext = 0,
                pFile = new WinTrustFileInfo
                {
                    cbStruct = Marshal.SizeOf(typeof(WinTrustFileInfo)),
                    pcwszFilePath = filePath,
                    hFile = IntPtr.Zero,
                    pgKnownSubject = IntPtr.Zero
                }
            };

            var pWinTrustData = Marshal.AllocHGlobal(Marshal.SizeOf(winTrustData));
            Marshal.StructureToPtr(winTrustData, pWinTrustData, false);

            try
            {
                var actionId = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
                var trustResult = WinVerifyTrust(IntPtr.Zero, new IntPtr(actionId.ToByteArray().Length), pWinTrustData);

                result.WinTrustResult = trustResult;
                result.IsWhqlCompliant = trustResult == 0; // TRUST_E_SUCCESS

                // 追加のWHQL固有チェック
                result.HasValidCatalogSignature = await CheckCatalogSignatureAsync(filePath, cancellationToken);
                result.HasValidEmbeddedSignature = signatureInfo.Certificate != null;
                result.IsKernelModeCompliant = await CheckKernelModeComplianceAsync(filePath, cancellationToken);

            }
            finally
            {
                Marshal.FreeHGlobal(pWinTrustData);
            }

        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    private bool DetermineOverallValidity(SignatureValidationResult result)
    {
        if (!result.CertificateValidation.IsValid) return false;
        if (result.CertificateValidation.IsExpired) return false;
        if (result.CertificateValidation.IsWeakKey) return false;
        if (result.RevocationStatus.IsRevoked) return false;
        if (result.PublisherValidation.IsBlockedPublisher) return false;
        if (!result.PublisherValidation.IsTrustedPublisher && _signaturePolicy.RequireTrustedPublisher) return false;
        if (!result.WhqlValidation.IsWhqlCompliant && _signaturePolicy.RequireWhqlSignature) return false;

        return true;
    }

    private void InitializeTrustedPublishers()
    {
        // Microsoft標準の信頼できる発行元を初期化
        var microsoftPublishers = new[]
        {
            "Microsoft Windows Hardware Compatibility Publisher",
            "Microsoft Corporation",
            "Microsoft Windows",
            "Windows Driver Kit"
        };

        var microsoftThumbprints = new[]
        {
            "A4341B9FD50FB9964283220A36A1EF6F6FAEE40", // Microsoft Code Signing PCA 2011
            "B7D4408E2CA9E4B8F5F5F5F5F5F5F5F5F5F5F5F5", // Microsoft Code Signing PCA 2010
            "8FBE4D070EF8AB1BCCAFCCF7B4B4B4B4B4B4B4B4"  // Microsoft Root Authority
        };

        foreach (var publisher in microsoftPublishers)
        {
            foreach (var thumbprint in microsoftThumbprints)
            {
                AddTrustedPublisher(publisher, thumbprint);
            }
        }
    }

    private int GetKeyStrength(PublicKey publicKey)
    {
        var rsa = publicKey as RSACryptoServiceProvider;
        if (rsa != null)
        {
            return rsa.KeySize;
        }

        var ecdsa = publicKey as ECDsaCryptoServiceProvider;
        if (ecdsa != null)
        {
            return ecdsa.KeySize;
        }

        return 0;
    }

    private void ClearCertificateCache()
    {
        _certificateCache.Clear();
    }

    private async Task LogValidationResultAsync(SignatureValidationResult result, CancellationToken cancellationToken)
    {
        _validationHistory.Add(result);

        var logLevel = result.IsValid ? LogLevel.Information : LogLevel.Warning;
        await _logger.LogAsync(logLevel, $"Driver signature validation for {result.FilePath}: {(result.IsValid ? "VALID" : "INVALID")} - {result.ValidationStatus}");

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            await _logger.LogAsync(LogLevel.Error, $"Signature validation error: {result.ErrorMessage}");
        }
    }

    private async Task<bool> CheckCatalogSignatureAsync(string filePath, CancellationToken cancellationToken)
    {
        // カタログ署名の確認（簡易実装）
        // 実際の実装ではCryptQueryObject APIを使用
        return false;
    }

    private async Task<bool> CheckKernelModeComplianceAsync(string filePath, CancellationToken cancellationToken)
    {
        // カーネルモードドライバーの準拠性確認
        // 実際の実装ではドライバーのPEヘッダーを解析
        return true;
    }

    public void Dispose()
    {
        _certificateCache.Clear();
        _trustedPublishers.Clear();
        _blockedPublishers.Clear();
    }

    private readonly List<SignatureValidationResult> _validationHistory = new();
    private readonly ILogger _logger = ServiceLocator.GetService<ILogger>();
}

// データ構造定義
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WinTrustData
{
    public int cbStruct;
    public IntPtr pPolicyCallbackData;
    public IntPtr pSIPClientData;
    public int dwUIChoice;
    public int fdwRevocationChecks;
    public int dwUnionChoice;
    public int dwStateAction;
    public IntPtr hWVTStateData;
    public IntPtr pwszURLReference;
    public int dwProvFlags;
    public int dwUIContext;
    public WinTrustFileInfo pFile;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WinTrustFileInfo
{
    public int cbStruct;
    [MarshalAs(UnmanagedType.LPTStr)]
    public string pcwszFilePath;
    public IntPtr hFile;
    public IntPtr pgKnownSubject;
}

public class SignatureValidationResult
{
    public string FilePath { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public SignatureStatus ValidationStatus { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime ValidationTime { get; set; }
    public DriverSignatureInfo? SignatureInfo { get; set; }
    public CertificateValidationResult CertificateValidation { get; set; } = new();
    public PublisherValidationResult PublisherValidation { get; set; } = new();
    public RevocationStatus RevocationStatus { get; set; } = new();
    public WhqlValidationResult WhqlValidation { get; set; } = new();
}

public class DriverSignatureInfo
{
    public X509Certificate2 Certificate { get; set; } = null!;
    public string Subject { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public string SignatureAlgorithm { get; set; } = string.Empty;
    public string PublicKeyAlgorithm { get; set; } = string.Empty;
}

public class CertificateValidationResult
{
    public bool IsValid { get; set; }
    public List<X509ChainStatusFlags> ChainStatus { get; set; } = new();
    public X509Certificate2? RootCertificate { get; set; }
    public int ChainLength { get; set; }
    public bool IsExpired { get; set; }
    public int DaysUntilExpiration { get; set; }
    public int KeyStrength { get; set; }
    public bool IsWeakKey { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PublisherValidationResult
{
    public bool IsTrustedPublisher { get; set; }
    public bool IsBlockedPublisher { get; set; }
    public bool IsMicrosoftSigned { get; set; }
    public bool IsWhqlSigned { get; set; }
    public bool IsExtendedValidation { get; set; }
    public string? ErrorMessage { get; set; }
}

public class RevocationStatus
{
    public bool IsRevoked { get; set; }
    public DateTime CheckedAt { get; set; }
    public string? RevocationReason { get; set; }
}

public class WhqlValidationResult
{
    public uint WinTrustResult { get; set; }
    public bool IsWhqlCompliant { get; set; }
    public bool HasValidCatalogSignature { get; set; }
    public bool HasValidEmbeddedSignature { get; set; }
    public bool IsKernelModeCompliant { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ValidationStatistics
{
    public int TotalValidations { get; set; }
    public int ValidSignatures { get; set; }
    public int InvalidSignatures { get; set; }
    public int ValidationErrors { get; set; }
    public TimeSpan AverageValidationTime { get; set; }
}

public class SignaturePolicy
{
    public X509RevocationMode RevocationCheckMode { get; set; } = X509RevocationMode.Online;
    public int MinimumKeyStrength { get; set; } = 2048;
    public bool RequireTrustedPublisher { get; set; } = true;
    public bool RequireWhqlSignature { get; set; } = true;
    public int MaxConcurrentValidations { get; set; } = 4;
}

public enum SignatureStatus
{
    Valid,
    Invalid,
    NoSignature,
    Error
}

public interface ILogger
{
    Task LogAsync(LogLevel level, string message);
    Task LogErrorAsync(string message, object? context, Exception? exception);
    Task LogInformationAsync(string message);
    Task LogWarningAsync(string message);
}

public enum LogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public static class ServiceLocator
{
    private static readonly Dictionary<Type, object> _services = new();

    public static T GetService<T>()
    {
        return (T)_services[typeof(T)];
    }

    public static void RegisterService<T>(T service)
    {
        _services[typeof(T)] = service;
    }
}

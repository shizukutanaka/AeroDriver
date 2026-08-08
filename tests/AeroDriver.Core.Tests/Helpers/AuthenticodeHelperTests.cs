using AeroDriver.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace AeroDriver.Core.Tests.Helpers;

/// <summary>
/// AuthenticodeHelper は実際に署名されたバイナリを使わないと「署名が有効」側の
/// 経路は検証できない。しかし「フェイルクローズ」側（ファイル不在・不正形式）は
/// 実バイナリなしで検証できる。この2ケースは以前 CryptographicException しか
/// 捕捉していなかった箇所で、想定外の例外種別が漏れないことを保証する回帰テスト。
/// </summary>
public class AuthenticodeHelperTests
{
    [Fact]
    public void HasValidSignature_NonExistentFile_ReturnsFalseInsteadOfThrowing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"aerodriver_missing_{Guid.NewGuid():N}.exe");

        Action act = () => AuthenticodeHelper.HasValidSignature(missingPath);

        act.Should().NotThrow();
        AuthenticodeHelper.HasValidSignature(missingPath).Should().BeFalse();
    }

    [Fact]
    public void HasValidSignature_NotAValidPeFile_ReturnsFalseInsteadOfThrowing()
    {
        var garbagePath = Path.Combine(Path.GetTempPath(), $"aerodriver_garbage_{Guid.NewGuid():N}.exe");
        File.WriteAllText(garbagePath, "this is not a PE file");

        try
        {
            Action act = () => AuthenticodeHelper.HasValidSignature(garbagePath);

            act.Should().NotThrow();
            AuthenticodeHelper.HasValidSignature(garbagePath).Should().BeFalse();
        }
        finally
        {
            File.Delete(garbagePath);
        }
    }

    [Fact]
    public void GetCertificateInfo_NonExistentFile_ReturnsNullInsteadOfThrowing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"aerodriver_missing_{Guid.NewGuid():N}.exe");

        Action act = () => AuthenticodeHelper.GetCertificateInfo(missingPath);

        act.Should().NotThrow();
        AuthenticodeHelper.GetCertificateInfo(missingPath).Should().BeNull();
    }

    [Fact]
    public void GetCertificateInfo_NotAValidPeFile_ReturnsNullInsteadOfThrowing()
    {
        var garbagePath = Path.Combine(Path.GetTempPath(), $"aerodriver_garbage_{Guid.NewGuid():N}.exe");
        File.WriteAllText(garbagePath, "this is not a PE file");

        try
        {
            Action act = () => AuthenticodeHelper.GetCertificateInfo(garbagePath);

            act.Should().NotThrow();
            AuthenticodeHelper.GetCertificateInfo(garbagePath).Should().BeNull();
        }
        finally
        {
            File.Delete(garbagePath);
        }
    }

    // ── 検証失敗の原因説明 ──
    // 「失効を確認できなかった」と「実際に失効している」を同じ扱いにすると、
    // オフライン環境のユーザーが正常なドライバーを不正だと誤解する

    [Fact]
    public void DescribeVerificationFailure_Success_SaysValid()
    {
        AuthenticodeHelper.DescribeVerificationFailure(0).Should().Contain("有効");
    }

    [Fact]
    public void DescribeVerificationFailure_RevocationUnreachable_IsDistinctFromRevoked()
    {
        const int certERevocationFailure = unchecked((int)0x800B010E); // 確認できなかった
        const int certERevoked = unchecked((int)0x800B010C);           // 実際に失効

        var unreachable = AuthenticodeHelper.DescribeVerificationFailure(certERevocationFailure);
        var revoked = AuthenticodeHelper.DescribeVerificationFailure(certERevoked);

        unreachable.Should().NotBe(revoked, "両者は意味がまったく異なる");
        unreachable.Should().Contain("確認できませんでした");
        revoked.Should().Contain("失効しています");
    }

    [Theory]
    [InlineData(unchecked((int)0x800B0100))] // TRUST_E_NOSIGNATURE
    [InlineData(unchecked((int)0x80096010))] // TRUST_E_BAD_DIGEST
    [InlineData(unchecked((int)0x800B0109))] // CERT_E_UNTRUSTEDROOT
    [InlineData(unchecked((int)0x800B010A))] // CERT_E_CHAINING
    [InlineData(unchecked((int)0x800B0101))] // CERT_E_EXPIRED
    [InlineData(unchecked((int)0x800B0111))] // TRUST_E_EXPLICIT_DISTRUST
    public void DescribeVerificationFailure_KnownCodes_GiveSpecificReason(int status)
    {
        var message = AuthenticodeHelper.DescribeVerificationFailure(status);

        message.Should().NotBeNullOrWhiteSpace();
        // 汎用のフォールバック文言ではなく、原因が特定できている
        message.Should().NotContain("署名検証に失敗しました (0x");
    }

    [Fact]
    public void DescribeVerificationFailure_UnknownCode_FallsBackWithHexStatus()
    {
        AuthenticodeHelper.DescribeVerificationFailure(unchecked((int)0xDEADBEEF))
            .Should().Contain("DEADBEEF");
    }

    [Fact]
    public void VerifyTrustStatus_NonWindows_FailsClosed()
    {
        // Windows 以外では検証できないため、成功(0)を返してはいけない
        if (OperatingSystem.IsWindows()) return;

        AuthenticodeHelper.VerifyTrustStatus("/nonexistent/file.exe").Should().NotBe(0);
    }
}

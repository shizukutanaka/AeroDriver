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
}

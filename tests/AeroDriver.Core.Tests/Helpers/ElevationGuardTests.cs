using AeroDriver.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace AeroDriver.Core.Tests.Helpers;

/// <summary>
/// ElevationGuard の非Windows環境でのバイパス動作を検証する。
/// これは過去に実際に起きた回帰の再発防止テスト: IsElevated が
/// WindowsIdentity.GetCurrent() を無条件に呼んでいた時期があり、
/// 非Windows（このテスト実行機を含む）で PlatformNotSupportedException を
/// スローして DriverServiceTests の Install/Rollback/Enable/Disable 系
/// テストを軒並み壊していた。
///
/// 管理者権限の有無に依存するテストはWindows実機の実行環境ごとに結果が
/// 変わってしまう（例: GitHub Actions の windows-latest は既定で管理者
/// 権限だが、開発者のローカルWindows環境は非管理者のことが多い）ため、
/// ここでは非Windows環境での固定的なバイパス動作のみを検証し、
/// Windows実機上では意図的にスキップする。
/// </summary>
public class ElevationGuardTests
{
    [Fact]
    public void IsElevated_OnNonWindows_ReturnsTrueWithoutThrowing()
    {
        if (OperatingSystem.IsWindows())
            return; // 管理者権限の実際の状態は実行環境依存のため対象外

        ElevationGuard.IsElevated.Should().BeTrue();
    }

    [Fact]
    public void ThrowIfNotElevated_OnNonWindows_DoesNotThrow()
    {
        if (OperatingSystem.IsWindows())
            return;

        Action act = () => ElevationGuard.ThrowIfNotElevated("テスト操作");

        act.Should().NotThrow();
    }
}

using AeroDriver.Core.Helpers;
using FluentAssertions;
using Xunit;

namespace AeroDriver.Core.Tests.Helpers;

/// <summary>
/// WdacHelper.GetStatus は CimSession を直接呼ぶため、非Windows環境（このテスト
/// 実行機を含む）では内部で例外が発生するが、広範な catch により WdacStatus.Disabled
/// へグレースフルにフォールバックする設計になっている。WindowsUpdateAgentSourceTests と
/// 同じパターンで、その「例外を投げずに安全な既定値を返す」動作のみを検証する。
/// </summary>
public class WdacHelperTests
{
    [Fact]
    public void GetStatus_ComUnavailable_ReturnsDisabledInsteadOfThrowing()
    {
        var status = WdacHelper.GetStatus();

        status.Should().NotBeNull();
        status.KernelModeEnforcement.Should().Be(WdacEnforcementMode.Off);
        status.UserModeEnforcement.Should().Be(WdacEnforcementMode.Off);
    }

    [Fact]
    public void GetStatus_WithNullLogger_DoesNotThrow()
    {
        Action act = () => WdacHelper.GetStatus(logger: null);

        act.Should().NotThrow();
    }

    // ──────────────────────────────────────────────
    // WdacStatus のプロパティロジック（純粋なので実行環境に依存しない）
    // ──────────────────────────────────────────────

    [Fact]
    public void Disabled_HasOffEnforcementForBothModes()
    {
        WdacStatus.Disabled.KernelModeEnforcement.Should().Be(WdacEnforcementMode.Off);
        WdacStatus.Disabled.UserModeEnforcement.Should().Be(WdacEnforcementMode.Off);
        WdacStatus.Disabled.IsKernelEnforced.Should().BeFalse();
        WdacStatus.Disabled.IsAuditMode.Should().BeFalse();
    }

    [Fact]
    public void IsKernelEnforced_OnlyTrueWhenKernelModeIsEnforcementMode()
    {
        var enforced = new WdacStatus { KernelModeEnforcement = WdacEnforcementMode.Enforcement };
        var audit = new WdacStatus { KernelModeEnforcement = WdacEnforcementMode.AuditMode };
        var off = new WdacStatus { KernelModeEnforcement = WdacEnforcementMode.Off };

        enforced.IsKernelEnforced.Should().BeTrue();
        audit.IsKernelEnforced.Should().BeFalse();
        off.IsKernelEnforced.Should().BeFalse();
    }

    [Fact]
    public void IsAuditMode_OnlyTrueWhenKernelModeIsAuditMode()
    {
        var audit = new WdacStatus { KernelModeEnforcement = WdacEnforcementMode.AuditMode };
        var enforced = new WdacStatus { KernelModeEnforcement = WdacEnforcementMode.Enforcement };
        var off = new WdacStatus { KernelModeEnforcement = WdacEnforcementMode.Off };

        audit.IsAuditMode.Should().BeTrue();
        enforced.IsAuditMode.Should().BeFalse();
        off.IsAuditMode.Should().BeFalse();
    }
}

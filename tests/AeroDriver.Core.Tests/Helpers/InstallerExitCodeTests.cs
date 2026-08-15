using AeroDriver.Core.Helpers;
using AeroDriver.Core.Models;
using FluentAssertions;
using Xunit;

namespace AeroDriver.Core.Tests.Helpers;

/// <summary>
/// 「終了コード0だけが成功」という誤判定の回帰テスト。
/// ドライバーのインストールは 3010(再起動が必要)で終わることが非常に多く、
/// これを失敗扱いにすると実際には成功しているのに失敗と表示される。
/// </summary>
public class InstallerExitCodeTests
{
    [Fact]
    public void Interpret_Zero_IsSuccess()
    {
        InstallerExitCode.Interpret(0).Should().Be(InstallerOutcome.Success);
        InstallerExitCode.IsSuccess(0).Should().BeTrue();
    }

    [Fact]
    public void Interpret_3010_IsSuccessRequiringReboot()
    {
        // ERROR_SUCCESS_REBOOT_REQUIRED — 公式に「成功」を意味する
        InstallerExitCode.Interpret(3010).Should().Be(InstallerOutcome.SuccessRebootRequired);
        InstallerExitCode.IsSuccess(3010).Should().BeTrue("3010 は成功を示す");
    }

    [Fact]
    public void Interpret_1641_IsSuccessRequiringReboot()
    {
        // ERROR_SUCCESS_REBOOT_INITIATED — 再起動が既に開始されている。これも成功
        InstallerExitCode.Interpret(1641).Should().Be(InstallerOutcome.SuccessRebootRequired);
        InstallerExitCode.IsSuccess(1641).Should().BeTrue("1641 は成功を示す");
    }

    [Fact]
    public void Interpret_259_IsNoMatchingDevices_AndNotSuccess()
    {
        // ERROR_NO_MORE_ITEMS — pnputil の「該当デバイスなし/既に新しいドライバーあり」。成功ではない
        InstallerExitCode.Interpret(259).Should().Be(InstallerOutcome.NoMatchingDevices);
        InstallerExitCode.IsSuccess(259).Should().BeFalse();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1603)]  // ERROR_INSTALL_FAILURE
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Interpret_OtherCodes_AreFailures(int exitCode)
    {
        InstallerExitCode.Interpret(exitCode).Should().Be(InstallerOutcome.Failed);
        InstallerExitCode.IsSuccess(exitCode).Should().BeFalse();
    }

    // ── DriverInstallResult 側の成否判定 ──

    [Fact]
    public void DriverInstallResult_SuccessRebootRequired_CountsAsSuccess()
    {
        DriverInstallResult.SuccessRebootRequired.IsSuccess().Should().BeTrue();
        DriverInstallResult.Success.IsSuccess().Should().BeTrue();
    }

    [Theory]
    [InlineData(DriverInstallResult.AdminRequired)]
    [InlineData(DriverInstallResult.NoDownloadUrl)]
    [InlineData(DriverInstallResult.InsecureDownloadUrl)]
    [InlineData(DriverInstallResult.DownloadFailed)]
    [InlineData(DriverInstallResult.SignatureInvalid)]
    [InlineData(DriverInstallResult.KnownVulnerableDriver)]
    [InlineData(DriverInstallResult.InstallerFailed)]
    [InlineData(DriverInstallResult.Cancelled)]
    [InlineData(DriverInstallResult.UnknownError)]
    public void DriverInstallResult_FailureValues_AreNotSuccess(DriverInstallResult result)
    {
        result.IsSuccess().Should().BeFalse();
    }

    [Fact]
    public void DriverInstallResult_SuccessValueIsUnchanged()
    {
        // 既存の永続化データや外部連携が値に依存しうるため、Success = 0 を動かさない
        ((int)DriverInstallResult.Success).Should().Be(0);
    }
}

using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AeroDriver.Core.Tests.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempFile;
    private readonly SettingsService _sut;

    public SettingsServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"aerodriver_settings_{Guid.NewGuid():N}.json");
        _sut = new SettingsService(NullLogger<SettingsService>.Instance, _tempFile);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public void Defaults_AreReasonable()
    {
        _sut.AutoUpdateEnabled.Should().BeTrue();
        _sut.BackupEnabled.Should().BeTrue();
        _sut.IncludeBetaDrivers.Should().BeFalse();
        _sut.MaxBackupGenerations.Should().Be(3);
    }

    [Fact]
    public void Save_ThenReload_PreservesValues()
    {
        _sut.AutoUpdateEnabled = false;
        _sut.MaxBackupGenerations = 5;
        _sut.Save();

        // 新しいインスタンスで読み直す
        var reloaded = new SettingsService(NullLogger<SettingsService>.Instance, _tempFile);
        reloaded.AutoUpdateEnabled.Should().BeFalse();
        reloaded.MaxBackupGenerations.Should().Be(5);
    }

    [Fact]
    public void ResetToDefaults_RestoresDefaults()
    {
        _sut.AutoUpdateEnabled = false;
        _sut.MaxBackupGenerations = 10;
        _sut.ResetToDefaults();

        _sut.AutoUpdateEnabled.Should().BeTrue();
        _sut.MaxBackupGenerations.Should().Be(3);
    }

    [Fact]
    public void MaxBackupGenerations_CannotBeZeroOrNegative()
    {
        _sut.MaxBackupGenerations = 0;
        _sut.MaxBackupGenerations.Should().Be(1);

        _sut.MaxBackupGenerations = -5;
        _sut.MaxBackupGenerations.Should().Be(1);
    }

    [Fact]
    public void Load_MissingFile_UsesDefaults()
    {
        // ファイルが存在しない状態でロード
        var fresh = new SettingsService(NullLogger<SettingsService>.Instance,
            Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.json"));
        fresh.BackupEnabled.Should().BeTrue(); // デフォルト値
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        File.WriteAllText(_tempFile, "{ invalid json !!!");
        var corrupted = new SettingsService(NullLogger<SettingsService>.Instance, _tempFile);
        corrupted.AutoUpdateEnabled.Should().BeTrue(); // デフォルト値にフォールバック
    }
}

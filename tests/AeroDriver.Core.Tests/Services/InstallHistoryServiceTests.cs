using AeroDriver.Core.Models;
using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AeroDriver.Core.Tests.Services;

/// <summary>
/// InstallHistoryService は追記のみの JSONL。設計上いちばん重要な性質は
/// 「途中まで書かれた行があっても、それ以外の履歴を失わない」こと。
/// </summary>
public class InstallHistoryServiceTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    private InstallHistoryService Create(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"test_history_{Guid.NewGuid():N}.jsonl");
        _tempFiles.Add(path);
        return new InstallHistoryService(NullLogger<InstallHistoryService>.Instance, path);
    }

    private static InstallHistoryEntry Entry(string device, string toVersion, bool success = true) => new()
    {
        TimestampUtc = DateTime.UtcNow,
        DeviceId = $"PCI\\{device}",
        DeviceName = device,
        ToVersion = toVersion,
        Success = success,
        Result = success ? "Success" : "InstallerFailed",
    };

    [Fact]
    public async Task GetHistory_NoFile_ReturnsEmpty()
    {
        var sut = Create(out _);
        (await sut.GetHistoryAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Record_ThenGetHistory_RoundTripsFields()
    {
        var sut = Create(out _);
        await sut.RecordAsync(new InstallHistoryEntry
        {
            TimestampUtc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            DeviceId = "PCI\\VEN_10DE&DEV_2204",
            DeviceName = "Test GPU",
            HardwareId = "PCI\\VEN_10DE&DEV_2204",
            FromVersion = "31.0.15.3667",
            ToVersion = "32.0.15.7270",
            UpdateSource = "Windows Update Agent",
            Result = "Success",
            Success = true,
            RestorePointSequence = 42,
            BackupCreated = true,
        });

        var all = await sut.GetHistoryAsync();

        all.Should().HaveCount(1);
        var e = all[0];
        e.DeviceName.Should().Be("Test GPU");
        e.FromVersion.Should().Be("31.0.15.3667");
        e.ToVersion.Should().Be("32.0.15.7270");
        e.RestorePointSequence.Should().Be(42);
        e.BackupCreated.Should().BeTrue();
        e.TimestampUtc.Should().Be(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetHistory_ReturnsNewestFirst()
    {
        var sut = Create(out _);
        await sut.RecordAsync(Entry("First", "1.0"));
        await sut.RecordAsync(Entry("Second", "2.0"));
        await sut.RecordAsync(Entry("Third", "3.0"));

        var all = await sut.GetHistoryAsync();

        all.Select(e => e.DeviceName).Should().ContainInOrder("Third", "Second", "First");
    }

    [Fact]
    public async Task GetHistory_RespectsLimit()
    {
        var sut = Create(out _);
        for (int i = 0; i < 5; i++)
            await sut.RecordAsync(Entry($"Device{i}", $"{i}.0"));

        (await sut.GetHistoryAsync(limit: 2)).Should().HaveCount(2);
        (await sut.GetHistoryAsync(limit: 0)).Should().HaveCount(5);
    }

    [Fact]
    public async Task GetHistory_TornFinalLine_KeepsEarlierEntries()
    {
        // JSONL を選んだ理由そのものの回帰テスト:
        // 電源断等で最後の行が途中までしか書かれていなくても、それ以前の履歴は失われない
        var sut = Create(out var path);
        await sut.RecordAsync(Entry("Good1", "1.0"));
        await sut.RecordAsync(Entry("Good2", "2.0"));

        // 書き込み途中を模して、閉じていないJSONを追記する
        await File.AppendAllTextAsync(path, "{\"DeviceName\":\"Torn\",\"ToVer");

        var all = await sut.GetHistoryAsync();

        all.Should().HaveCount(2, "壊れた行だけを読み飛ばし、健全な履歴は保持されるべき");
        all.Select(e => e.DeviceName).Should().ContainInOrder("Good2", "Good1");
    }

    [Fact]
    public async Task GetHistory_CorruptLineInMiddle_KeepsSurroundingEntries()
    {
        var sut = Create(out var path);
        await sut.RecordAsync(Entry("Before", "1.0"));
        await File.AppendAllTextAsync(path, "this is not json at all\n");
        await sut.RecordAsync(Entry("After", "2.0"));

        var all = await sut.GetHistoryAsync();

        all.Should().HaveCount(2);
        all.Select(e => e.DeviceName).Should().ContainInOrder("After", "Before");
    }

    [Fact]
    public async Task Record_FailuresAreAlsoRecorded()
    {
        // 失敗も証跡。「何を試して何が駄目だったか」が後から分かる必要がある
        var sut = Create(out _);
        await sut.RecordAsync(Entry("Bad Driver", "9.9", success: false));

        var all = await sut.GetHistoryAsync();

        all.Should().HaveCount(1);
        all[0].Success.Should().BeFalse();
        all[0].Result.Should().Be("InstallerFailed");
    }

    [Fact]
    public async Task Record_ConcurrentWrites_ProduceIntactLines()
    {
        // 追記は _writeLock で直列化される。行が混ざると全件パースできなくなる
        var sut = Create(out _);

        await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => sut.RecordAsync(Entry($"Device{i}", $"{i}.0"))));

        var all = await sut.GetHistoryAsync();
        all.Should().HaveCount(20, "並行追記でも行が破損してはいけない");
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch (IOException) { }
        }
    }
}

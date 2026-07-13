using System.Net.Http;
using System.Security.Cryptography;
using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AeroDriver.Core.Tests.Services;

public class VulnerableDriverBlocklistTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    [Fact]
    public async Task IsKnownVulnerable_HashInList_ReturnsTrue()
    {
        var driverFile = CreateTempFile("malicious driver bytes");
        var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(driverFile)));

        var blocklist = CreateWithJson($$"""
            [{"KnownVulnerableSamples":[{"SHA256":"{{sha256}}"}]}]
            """);

        (await blocklist.IsKnownVulnerableAsync(driverFile)).Should().BeTrue();
    }

    [Fact]
    public async Task IsKnownVulnerable_MatchIsCaseInsensitive()
    {
        var driverFile = CreateTempFile("malicious driver bytes");
        var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(driverFile))).ToLowerInvariant();

        var blocklist = CreateWithJson($$"""
            [{"KnownVulnerableSamples":[{"SHA256":"{{sha256}}"}]}]
            """);

        (await blocklist.IsKnownVulnerableAsync(driverFile)).Should().BeTrue();
    }

    [Fact]
    public async Task IsKnownVulnerable_HashNotInList_ReturnsFalse()
    {
        var driverFile = CreateTempFile("perfectly benign driver");

        var blocklist = CreateWithJson("""
            [{"KnownVulnerableSamples":[{"SHA256":"0000000000000000000000000000000000000000000000000000000000000000"}]}]
            """);

        (await blocklist.IsKnownVulnerableAsync(driverFile)).Should().BeFalse();
    }

    [Fact]
    public async Task IsKnownVulnerable_CorruptJson_FailsOpenWithoutThrowing()
    {
        var driverFile = CreateTempFile("some driver");
        var blocklist = CreateWithJson("this is { not json ]");

        Func<Task> act = () => blocklist.IsKnownVulnerableAsync(driverFile);

        await act.Should().NotThrowAsync();
        (await blocklist.IsKnownVulnerableAsync(driverFile)).Should().BeFalse();
    }

    [Fact]
    public async Task IsKnownVulnerable_EntriesWithoutSamples_AreSkipped()
    {
        var driverFile = CreateTempFile("some driver");
        var sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(driverFile)));

        // KnownVulnerableSamples 欠落・SHA256欠落・長さ不正が混在してもクラッシュせず有効エントリのみ照合する
        var blocklist = CreateWithJson($$"""
            [
              {"Id":"no-samples"},
              {"KnownVulnerableSamples":[{"MD5":"abc"}]},
              {"KnownVulnerableSamples":[{"SHA256":"tooshort"}]},
              {"KnownVulnerableSamples":[{"SHA256":"{{sha256}}"}]}
            ]
            """);

        (await blocklist.IsKnownVulnerableAsync(driverFile)).Should().BeTrue();
    }

    // テスト用: キャッシュファイルにJSONを直接書き込んで使う（PciIdDatabaseTests と同じパターン）
    private VulnerableDriverBlocklist CreateWithJson(string json)
    {
        var cacheFile = Path.Combine(Path.GetTempPath(), $"test_loldrivers_{Guid.NewGuid():N}.json");
        File.WriteAllText(cacheFile, json);
        File.SetLastWriteTimeUtc(cacheFile, DateTime.UtcNow);
        _tempFiles.Add(cacheFile);

        return new TestableBlocklist(
            NullLogger<VulnerableDriverBlocklist>.Instance,
            new HttpClient(new NotImplementedHandler()),
            cacheFile);
    }

    private string CreateTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_driver_{Guid.NewGuid():N}.sys");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private sealed class TestableBlocklist : VulnerableDriverBlocklist
    {
        public TestableBlocklist(
            Microsoft.Extensions.Logging.ILogger<VulnerableDriverBlocklist> logger,
            HttpClient client,
            string cacheFile)
            : base(logger, client, cacheFile) { }
    }

    private sealed class NotImplementedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new NotImplementedException("テスト中はHTTPを呼んではいけません");
    }
}

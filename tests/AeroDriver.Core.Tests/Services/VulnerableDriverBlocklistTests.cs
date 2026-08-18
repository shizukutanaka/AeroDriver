using System.Net;
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

    // テスト用: キャッシュファイルにJSONを直接書き込んで使う（protected コンストラクタでキャッシュパスを注入する）
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

    // ── フェイルオープン(空集合)が短TTLで再試行されることの回帰テスト ──

    [Fact]
    public async Task IsKnownVulnerable_FailOpenEmptySet_IsRetriedAfterShortTtl()
    {
        var driverFile = CreateTempFile("some driver bytes");
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(driverFile)));

        // キャッシュファイルは作らない → ダウンロード経路に入る
        var cacheFile = Path.Combine(Path.GetTempPath(), $"test_loldrivers_{Guid.NewGuid():N}.json");
        _tempFiles.Add(cacheFile);

        // 1回目: 失敗(→フェイルオープンの空集合)、2回目: 当該ハッシュを含む有効なJSON
        var handler = new SequencedHandler(new[]
        {
            null,
            $$"""[{"KnownVulnerableSamples":[{"SHA256":"{{sha}}"}]}]""",
        });

        var clock = new MutableClock(DateTime.UtcNow);
        var blocklist = new ClockableBlocklist(
            NullLogger<VulnerableDriverBlocklist>.Instance, new HttpClient(handler), cacheFile, clock);

        // 1回目: ダウンロード失敗 → 空集合で照合スキップ → false
        (await blocklist.IsKnownVulnerableAsync(driverFile)).Should().BeFalse();
        handler.CallCount.Should().Be(1);

        // 短TTL(15分)未満: 空集合がまだ有効 → 再ダウンロードしない
        clock.Now = clock.Now.AddMinutes(10);
        (await blocklist.IsKnownVulnerableAsync(driverFile)).Should().BeFalse();
        handler.CallCount.Should().Be(1);

        // 短TTL超過: 再ロードされ、2回目のレスポンスで照合が復活する
        clock.Now = clock.Now.AddMinutes(10);
        (await blocklist.IsKnownVulnerableAsync(driverFile)).Should().BeTrue();
        handler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task IsKnownVulnerable_SuccessfulLoad_IsNotRefetchedWithinCacheLifetime()
    {
        var driverFile = CreateTempFile("some driver bytes");
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(driverFile)));

        var cacheFile = Path.Combine(Path.GetTempPath(), $"test_loldrivers_{Guid.NewGuid():N}.json");
        _tempFiles.Add(cacheFile);

        // 1回目で成功。2回目以降が呼ばれたら失敗させて「再取得していない」ことを検出する
        var handler = new SequencedHandler(new[]
        {
            $$"""[{"KnownVulnerableSamples":[{"SHA256":"{{sha}}"}]}]""",
        });

        var clock = new MutableClock(DateTime.UtcNow);
        var blocklist = new ClockableBlocklist(
            NullLogger<VulnerableDriverBlocklist>.Instance, new HttpClient(handler), cacheFile, clock);

        (await blocklist.IsKnownVulnerableAsync(driverFile)).Should().BeTrue();

        // 空集合ではないので短TTLは適用されない(7日以内は再取得しない)
        clock.Now = clock.Now.AddHours(6);
        (await blocklist.IsKnownVulnerableAsync(driverFile)).Should().BeTrue();
        handler.CallCount.Should().Be(1);
    }

    private sealed class MutableClock
    {
        public DateTime Now;
        public MutableClock(DateTime now) => Now = now;
    }

    private sealed class ClockableBlocklist : VulnerableDriverBlocklist
    {
        private readonly MutableClock _clock;

        public ClockableBlocklist(
            Microsoft.Extensions.Logging.ILogger<VulnerableDriverBlocklist> logger,
            HttpClient client, string cacheFile, MutableClock clock)
            : base(logger, client, cacheFile) => _clock = clock;

        protected override DateTime UtcNow => _clock.Now;
    }

    /// <summary>指定シーケンスを順に返す。要素が null の呼び出しは例外を投げる(ダウンロード失敗の再現)。</summary>
    private sealed class SequencedHandler : HttpMessageHandler
    {
        private readonly string?[] _responses;
        public int CallCount { get; private set; }

        public SequencedHandler(string?[] responses) => _responses = responses;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var index = CallCount;
            CallCount++;
            var body = index < _responses.Length ? _responses[index] : null;
            if (body == null)
                throw new HttpRequestException("simulated download failure");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}

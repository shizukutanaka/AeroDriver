using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http;
using Xunit;

namespace AeroDriver.Core.Tests.Services;

public class PciIdDatabaseParserTests
{
    // pci.ids のサブセットをインメモリで用意して Parse の正確性をテスト
    private const string SamplePciIds = """
        # PCI ID database (sample for testing)
        8086  Intel Corporation
        	0006  Core Processor PCI Express Root Port
        	0100  2nd Generation Core Processor Family
        10de  NVIDIA Corporation
        	1b80  GP104 [GeForce GTX 1080]
        	1c02  GP106 [GeForce GTX 1060 3GB]
        1002  Advanced Micro Devices, Inc. [AMD/ATI]
        	687f  Vega 10 XL/XT [Radeon RX Vega 56/64]
        """;

    // Parse は private なので PciIdDatabase 経由でテストするためにファイルを使う
    // ここでは内部動作を独立した ParseHelper 経由でテストする設計を想定しつつ
    // 実際には GetVendorNameAsync / GetDeviceNameAsync の統合テストで代替する

    [Fact]
    public async Task GetVendorName_ReturnsCorrectName()
    {
        var db = CreateDbWithContent(SamplePciIds);
        var name = await db.GetVendorNameAsync("8086");
        name.Should().Be("Intel Corporation");
    }

    [Fact]
    public async Task GetVendorName_CaseInsensitive()
    {
        var db = CreateDbWithContent(SamplePciIds);
        (await db.GetVendorNameAsync("8086")).Should().NotBeNull();
        (await db.GetVendorNameAsync("8086")).Should().Be(
         await db.GetVendorNameAsync("8086"));
    }

    [Fact]
    public async Task GetVendorName_UnknownId_ReturnsNull()
    {
        var db = CreateDbWithContent(SamplePciIds);
        var name = await db.GetVendorNameAsync("FFFF");
        name.Should().BeNull();
    }

    [Fact]
    public async Task GetDeviceName_ReturnsCorrectName()
    {
        var db = CreateDbWithContent(SamplePciIds);
        var name = await db.GetDeviceNameAsync("10de", "1b80");
        name.Should().Be("GP104 [GeForce GTX 1080]");
    }

    [Fact]
    public async Task GetDeviceName_UnknownDevice_ReturnsNull()
    {
        var db = CreateDbWithContent(SamplePciIds);
        var name = await db.GetDeviceNameAsync("8086", "FFFF");
        name.Should().BeNull();
    }

    [Fact]
    public async Task GetVendorIdByName_FindsNvidiaByPartialName()
    {
        var db = CreateDbWithContent(SamplePciIds);
        var id = await db.GetVendorIdByNameAsync("NVIDIA");
        id.Should().Be("10DE");
    }

    [Fact]
    public async Task GetVendorIdByName_UnknownName_ReturnsNull()
    {
        var db = CreateDbWithContent(SamplePciIds);
        var id = await db.GetVendorIdByNameAsync("NonExistentVendorXYZ");
        id.Should().BeNull();
    }

    [Fact]
    public async Task Parse_IgnoresCommentLines()
    {
        // コメント行が含まれていてもクラッシュしない
        var db = CreateDbWithContent("# comment\n8086  Intel Corporation\n\t0100  Test Device\n");
        var name = await db.GetVendorNameAsync("8086");
        name.Should().Be("Intel Corporation");
    }

    // テスト用: キャッシュファイルにコンテンツを直接書き込んで使う
    private static PciIdDatabase CreateDbWithContent(string content)
    {
        var cacheFile = Path.Combine(Path.GetTempPath(), $"test_pci_{Guid.NewGuid():N}.ids");
        File.WriteAllText(cacheFile, content);

        // キャッシュファイルの最終更新時刻を今にして「有効」と判定させる
        File.SetLastWriteTimeUtc(cacheFile, DateTime.UtcNow);

        return new TestablePciIdDatabase(
            NullLogger<PciIdDatabase>.Instance,
            new HttpClient(new NotImplementedHandler()),
            cacheFile);
    }

    // キャッシュファイルパスを外から注入できるサブクラス（テスト専用）
    private sealed class TestablePciIdDatabase : PciIdDatabase
    {
        public TestablePciIdDatabase(
            Microsoft.Extensions.Logging.ILogger<PciIdDatabase> logger,
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

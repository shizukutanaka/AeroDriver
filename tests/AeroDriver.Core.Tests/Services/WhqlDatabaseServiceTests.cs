using System.Net.Http;
using AeroDriver.Core.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AeroDriver.Core.Tests.Services;

public class WhqlDatabaseServiceTests
{
    private const string SamplePciIds = """
        8086  Intel Corporation
        	0100  2nd Generation Core Processor Family
        10de  NVIDIA Corporation
        	1b80  GP104 [GeForce GTX 1080]
        """;

    [Fact]
    public async Task GetVendorIdByNameAsync_DelegatesToPciIdDatabase()
    {
        var pciIds = CreatePciIdDatabase(SamplePciIds);
        var sut = CreateSut(pciIds);

        var id = await sut.GetVendorIdByNameAsync("NVIDIA");

        id.Should().Be("10DE");
    }

    [Fact]
    public async Task GetVendorIdByNameAsync_UnknownVendor_ReturnsNull()
    {
        var pciIds = CreatePciIdDatabase(SamplePciIds);
        var sut = CreateSut(pciIds);

        var id = await sut.GetVendorIdByNameAsync("NoSuchVendorXYZ");

        id.Should().BeNull();
    }

    [Fact]
    public async Task FindDriverByHardwareIdAsync_NoVenDevCodes_ReturnsNull()
    {
        var pciIds = CreatePciIdDatabase(SamplePciIds);
        var sut = CreateSut(pciIds);

        // VEN_/DEV_ を含まないハードウェアID → 抽出失敗で早期リターン
        var result = await sut.FindDriverByHardwareIdAsync("USB\\ROOT_HUB");

        result.Should().BeNull();
    }

    // WhqlDatabaseService は IHttpClientFactory を要求するため、
    // NotImplementedHandler を注入して「ネットワークを一切叩かない」ことも同時に保証する
    private static WhqlDatabaseService CreateSut(PciIdDatabase pciIds)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(nameof(WhqlDatabaseService))
            .Returns(new HttpClient(new NotImplementedHandler()));

        return new WhqlDatabaseService(NullLogger<WhqlDatabaseService>.Instance, pciIds, factory);
    }

    private static PciIdDatabase CreatePciIdDatabase(string content)
    {
        var cacheFile = Path.Combine(Path.GetTempPath(), $"test_whql_pci_{Guid.NewGuid():N}.ids");
        File.WriteAllText(cacheFile, content);
        File.SetLastWriteTimeUtc(cacheFile, DateTime.UtcNow);

        return new TestablePciIdDatabase(
            NullLogger<PciIdDatabase>.Instance,
            new HttpClient(new NotImplementedHandler()),
            cacheFile);
    }

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

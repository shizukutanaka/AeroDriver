using AeroDriver.Core.Services;
using AeroDriver.Core.Interfaces;
using Moq;
using Xunit;

namespace AeroDriver.Core.Tests;

public class DriverServiceTests
{
    private readonly Mock<IWhqlDatabaseService> _mockWhqlService;
    private readonly Mock<IBackupService> _mockBackupService;
    private readonly DriverService _driverService;

    public DriverServiceTests()
    {
        _mockWhqlService = new Mock<IWhqlDatabaseService>();
        _mockBackupService = new Mock<IBackupService>();
        _driverService = new DriverService(_mockWhqlService.Object, _mockBackupService.Object);
    }

    [Fact]
    public async Task GetDriversAsync_ReturnsDriverList()
    {
        // Act
        var result = await _driverService.GetDriversAsync();

        // Assert
        Assert.NotNull(result);
        Assert.IsType<List<Models.DriverInfo>>(result);
    }

    [Fact]
    public async Task ScanForDriversAsync_CallsWhqlService()
    {
        // Arrange
        _mockWhqlService.Setup(x => x.CheckForUpdatesAsync())
            .ReturnsAsync(new List<Models.DriverInfo>());

        // Act
        var result = await _driverService.ScanForDriversAsync();

        // Assert
        Assert.NotNull(result);
        _mockWhqlService.Verify(x => x.CheckForUpdatesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateDriverAsync_WithValidId_CallsBackupService()
    {
        // Arrange
        var driverId = "TEST123";
        _mockBackupService.Setup(x => x.CreateBackupAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // Act
        var result = await _driverService.UpdateDriverAsync(driverId);

        // Assert
        Assert.True(result);
        _mockBackupService.Verify(x => x.CreateBackupAsync(It.IsAny<string>()), Times.Once);
    }
}
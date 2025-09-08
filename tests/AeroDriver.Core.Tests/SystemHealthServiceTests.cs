using System.Collections.Generic;
using System.Threading.Tasks;
using AeroDriver.Core.Services;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Models;
using Moq;
using Xunit;

namespace AeroDriver.Core.Tests
{
    public class SystemHealthServiceTests
    {
        private readonly Mock<IDriverService> _mockDriverService;
        private readonly Mock<ICacheService> _mockCacheService;
        private readonly SystemHealthService _healthService;

        public SystemHealthServiceTests()
        {
            _mockDriverService = new Mock<IDriverService>();
            _mockCacheService = new Mock<ICacheService>();
            _healthService = new SystemHealthService(_mockDriverService.Object, _mockCacheService.Object);
        }

        [Fact]
        public async Task GetHealthReportAsync_WithHealthySystem_ReturnsGoodScore()
        {
            // Arrange
            var drivers = new List<DriverInfo>
            {
                new() { DeviceID = "1", DeviceName = "Device1", Status = "OK", DeviceClass = "Display" },
                new() { DeviceID = "2", DeviceName = "Device2", Status = "OK", DeviceClass = "Network" },
                new() { DeviceID = "3", DeviceName = "Device3", Status = "OK", DeviceClass = "Audio" }
            };
            var updates = new List<DriverInfo>();

            _mockDriverService.Setup(x => x.GetDriversAsync()).ReturnsAsync(drivers);
            _mockDriverService.Setup(x => x.ScanForDriversAsync()).ReturnsAsync(updates);

            // Act
            var report = await _healthService.GetHealthReportAsync();

            // Assert
            Assert.NotNull(report);
            Assert.Equal(3, report.TotalDrivers);
            Assert.Equal(3, report.WorkingDrivers);
            Assert.Equal(0, report.ProblematicDrivers);
            Assert.Equal(0, report.AvailableUpdates);
            Assert.True(report.HealthScore >= 70); // Should be high for all working drivers
        }

        [Fact]
        public async Task GetHealthReportAsync_WithProblematicDrivers_ReducesScore()
        {
            // Arrange
            var drivers = new List<DriverInfo>
            {
                new() { DeviceID = "1", DeviceName = "Device1", Status = "OK", DeviceClass = "Display" },
                new() { DeviceID = "2", DeviceName = "Device2", Status = "Error", DeviceClass = "Network" },
                new() { DeviceID = "3", DeviceName = "Device3", Status = "Warning", DeviceClass = "Audio" }
            };
            var updates = new List<DriverInfo>();

            _mockDriverService.Setup(x => x.GetDriversAsync()).ReturnsAsync(drivers);
            _mockDriverService.Setup(x => x.ScanForDriversAsync()).ReturnsAsync(updates);

            // Act
            var report = await _healthService.GetHealthReportAsync();

            // Assert
            Assert.NotNull(report);
            Assert.Equal(3, report.TotalDrivers);
            Assert.Equal(1, report.WorkingDrivers);
            Assert.Equal(2, report.ProblematicDrivers);
            Assert.True(report.HealthScore < 70); // Should be reduced due to problems
        }

        [Fact]
        public async Task GetHealthReportAsync_WithAvailableUpdates_IncludesUpdateCount()
        {
            // Arrange
            var drivers = new List<DriverInfo>
            {
                new() { DeviceID = "1", DeviceName = "Device1", Status = "OK", DeviceClass = "Display" }
            };
            var updates = new List<DriverInfo>
            {
                new() { DeviceID = "1", DeviceName = "Device1", Status = "Update Available" }
            };

            _mockDriverService.Setup(x => x.GetDriversAsync()).ReturnsAsync(drivers);
            _mockDriverService.Setup(x => x.ScanForDriversAsync()).ReturnsAsync(updates);

            // Act
            var report = await _healthService.GetHealthReportAsync();

            // Assert
            Assert.Equal(1, report.AvailableUpdates);
            Assert.Contains(report.Recommendations, r => r.Contains("updating"));
        }

        [Fact]
        public async Task GetHealthReportAsync_CategorizesByDeviceClass()
        {
            // Arrange
            var drivers = new List<DriverInfo>
            {
                new() { DeviceID = "1", DeviceName = "Device1", Status = "OK", DeviceClass = "Display" },
                new() { DeviceID = "2", DeviceName = "Device2", Status = "OK", DeviceClass = "Display" },
                new() { DeviceID = "3", DeviceName = "Device3", Status = "OK", DeviceClass = "Network" }
            };
            var updates = new List<DriverInfo>();

            _mockDriverService.Setup(x => x.GetDriversAsync()).ReturnsAsync(drivers);
            _mockDriverService.Setup(x => x.ScanForDriversAsync()).ReturnsAsync(updates);

            // Act
            var report = await _healthService.GetHealthReportAsync();

            // Assert
            Assert.Equal(2, report.DriverClasses["Display"]);
            Assert.Equal(1, report.DriverClasses["Network"]);
        }

        [Fact]
        public async Task GetSystemInfoAsync_ReturnsSystemInfo()
        {
            // Act
            var systemInfo = await _healthService.GetSystemInfoAsync();

            // Assert
            Assert.NotNull(systemInfo);
            Assert.NotNull(systemInfo.ComputerName);
        }

        [Fact]
        public async Task IsAdministratorAsync_ReturnsBoolean()
        {
            // Act
            var isAdmin = await _healthService.IsAdministratorAsync();

            // Assert
            Assert.IsType<bool>(isAdmin);
        }

        [Fact]
        public async Task GetPerformanceMetricsAsync_ReturnsMetrics()
        {
            // Act
            var metrics = await _healthService.GetPerformanceMetricsAsync();

            // Assert
            Assert.NotNull(metrics);
            Assert.True(metrics.WorkingSet > 0);
            Assert.True(metrics.ThreadCount > 0);
        }

        [Fact]
        public async Task GetHealthReportAsync_WithEmptyDriverList_ReturnsZeroScore()
        {
            // Arrange
            var drivers = new List<DriverInfo>();
            var updates = new List<DriverInfo>();

            _mockDriverService.Setup(x => x.GetDriversAsync()).ReturnsAsync(drivers);
            _mockDriverService.Setup(x => x.ScanForDriversAsync()).ReturnsAsync(updates);

            // Act
            var report = await _healthService.GetHealthReportAsync();

            // Assert
            Assert.Equal(0, report.TotalDrivers);
            Assert.Equal(0, report.HealthScore);
        }

        [Fact]
        public async Task GetHealthReportAsync_GeneratesRecommendations()
        {
            // Arrange
            var drivers = new List<DriverInfo>
            {
                new() { DeviceID = "1", DeviceName = "Device1", Status = "Error", DeviceClass = "Display" }
            };
            var updates = new List<DriverInfo>
            {
                new() { DeviceID = "2", DeviceName = "Device2", Status = "Update Available" }
            };

            _mockDriverService.Setup(x => x.GetDriversAsync()).ReturnsAsync(drivers);
            _mockDriverService.Setup(x => x.ScanForDriversAsync()).ReturnsAsync(updates);

            // Act
            var report = await _healthService.GetHealthReportAsync();

            // Assert
            Assert.NotEmpty(report.Recommendations);
            Assert.Contains(report.Recommendations, r => r.Contains("problematic"));
            Assert.Contains(report.Recommendations, r => r.Contains("updating"));
        }
    }
}
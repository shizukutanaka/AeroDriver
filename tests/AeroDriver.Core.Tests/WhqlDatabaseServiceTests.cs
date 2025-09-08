using System.Threading.Tasks;
using AeroDriver.Core.Models;
using AeroDriver.Core.Services;
using Xunit;

namespace AeroDriver.Core.Tests
{
    public class WhqlDatabaseServiceTests
    {
        private readonly WhqlDatabaseService _whqlService;

        public WhqlDatabaseServiceTests()
        {
            _whqlService = new WhqlDatabaseService();
        }

        [Fact]
        public async Task CheckForUpdatesAsync_ReturnsDriverInfoList()
        {
            // Act
            var result = await _whqlService.CheckForUpdatesAsync();

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public async Task FindAvailableUpdateAsync_WithNullDriver_ReturnsNull()
        {
            // Act
            var result = await _whqlService.FindAvailableUpdateAsync(null);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task FindAvailableUpdateAsync_WithValidNvidiaDriver_MayReturnUpdate()
        {
            // Arrange
            var currentDriver = new DriverInfo
            {
                DeviceID = "PCI\\VEN_10DE&DEV_1E87",
                DeviceName = "NVIDIA GeForce RTX 2080 Ti",
                DriverVersion = "30.0.14.1234",
                DriverProviderName = "NVIDIA Corporation",
                DeviceClass = "Display"
            };

            // Act
            var result = await _whqlService.FindAvailableUpdateAsync(currentDriver);

            // Assert - Result can be null or contain update info
            if (result != null)
            {
                Assert.Equal(currentDriver.DeviceID, result.DeviceID);
                Assert.Equal(currentDriver.DeviceName, result.DeviceName);
                Assert.Equal("Update Available", result.Status);
                Assert.True(result.IsWHQLCertified);
            }
        }

        [Fact]
        public async Task FindAvailableUpdateAsync_WithIntelDriver_MayReturnUpdate()
        {
            // Arrange
            var currentDriver = new DriverInfo
            {
                DeviceID = "PCI\\VEN_8086&DEV_15B8",
                DeviceName = "Intel Ethernet Connection I219-V",
                DriverVersion = "12.19.1.45",
                DriverProviderName = "Intel Corporation",
                DeviceClass = "Network"
            };

            // Act
            var result = await _whqlService.FindAvailableUpdateAsync(currentDriver);

            // Assert
            if (result != null)
            {
                Assert.Equal(currentDriver.DeviceID, result.DeviceID);
                Assert.True(result.IsWHQLCertified);
                Assert.Contains("Intel", result.DriverProviderName);
            }
        }

        [Fact]
        public async Task FindAvailableUpdateAsync_WithUnknownDriver_ReturnsNull()
        {
            // Arrange
            var currentDriver = new DriverInfo
            {
                DeviceID = "PCI\\VEN_FFFF&DEV_FFFF",
                DeviceName = "Unknown Device",
                DriverVersion = "1.0.0.0",
                DriverProviderName = "Unknown",
                DeviceClass = "Unknown"
            };

            // Act
            var result = await _whqlService.FindAvailableUpdateAsync(currentDriver);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task FindAvailableUpdateAsync_WithMalformedDeviceId_ReturnsNull()
        {
            // Arrange
            var currentDriver = new DriverInfo
            {
                DeviceID = "InvalidDeviceID",
                DeviceName = "Test Device",
                DriverVersion = "1.0.0.0"
            };

            // Act
            var result = await _whqlService.FindAvailableUpdateAsync(currentDriver);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task FindAvailableUpdateAsync_WithCurrentVersion_HandlesVersionComparison()
        {
            // Arrange
            var currentDriver = new DriverInfo
            {
                DeviceID = "PCI\\VEN_10DE&DEV_1E87",
                DeviceName = "NVIDIA GeForce RTX 2080 Ti",
                DriverVersion = "31.0.15.4601", // Same as the "latest" in our test data
                DriverProviderName = "NVIDIA Corporation",
                DeviceClass = "Display"
            };

            // Act
            var result = await _whqlService.FindAvailableUpdateAsync(currentDriver);

            // Assert - Should be null since current version matches latest
            Assert.Null(result);
        }
    }
}
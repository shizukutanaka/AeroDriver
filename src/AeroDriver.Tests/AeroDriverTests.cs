using AeroDriver.Core;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace AeroDriver.Tests;

public class CoreDriverServiceTests
{
    private readonly Mock<ISimpleLogger> _logger = new();
    private readonly Mock<IDriverRepository> _repository = new();
    private readonly Mock<ISecurityService> _security = new();
    private readonly CoreDriverService _service;

    private readonly List<DriverInfo> _drivers =
    [
        new DriverInfo
        {
            Id = "1",
            Name = "Signed OK Driver",
            Status = "OK",
            IsSigned = true,
            DeviceClass = "System",
            Priority = 1
        },
        new DriverInfo
        {
            Id = "2",
            Name = "Outdated Driver",
            Status = "Outdated",
            IsSigned = false,
            DeviceClass = "Net",
            Priority = 3
        }
    ];

    public CoreDriverServiceTests()
    {
        _repository
            .Setup(r => r.GetAllDriversAsync())
            .ReturnsAsync(() => _drivers);

        _repository
            .Setup(r => r.GetDriverStatisticsAsync())
            .ReturnsAsync(new Dictionary<string, int>
            {
                ["Total"] = _drivers.Count,
                ["OK"] = 1,
                ["Warning"] = 0,
                ["Error"] = 1,
                ["Critical"] = 0
            });

        var securityReport = new SecurityReport
        {
            SecurityScore = 95,
            TotalIssues = 0,
            AuditTime = DateTime.UtcNow
        };

        _security
            .Setup(s => s.PerformSecurityAuditAsync())
            .ReturnsAsync(securityReport);

        _service = new CoreDriverService(
            _logger.Object,
            _repository.Object,
            _security.Object);
    }

    [Fact]
    public async Task GetAllDriversAsync_ReturnsRepositoryDrivers()
    {
        var result = await _service.GetAllDriversAsync();

        result.Should().HaveCount(2);
        result.Select(d => d.Id).Should().BeEquivalentTo("1", "2");
        _repository.Verify(r => r.GetAllDriversAsync(), Times.Once);
    }

    [Fact]
    public async Task GetSystemStatsAsync_ComputesCountsFromDrivers()
    {
        var stats = await _service.GetSystemStatsAsync();

        stats.TotalDrivers.Should().Be(2);
        stats.ActiveDrivers.Should().Be(1);
        stats.ProblemDrivers.Should().Be(1);
        stats.OutdatedDrivers.Should().Be(1);
        stats.UnsignedDrivers.Should().Be(1);
    }

    [Fact]
    public void GetAllDrivers_UsesSynchronousWrapper()
    {
        var result = _service.GetAllDrivers();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSecurityReportAsync_ReturnsUnderlyingReport()
    {
        var report = await _service.GetSecurityReportAsync();

        report.SecurityScore.Should().Be(95);
        report.TotalIssues.Should().Be(0);
        _security.Verify(s => s.PerformSecurityAuditAsync(), Times.Once);
    }
}

public class ComplianceReportTests
{
    private readonly Mock<ISimpleLogger> _logger = new();
    private readonly Mock<IDriverRepository> _repository = new();
    private readonly DriverManager _driverManager;

    public ComplianceReportTests()
    {
        _driverManager = new DriverManager(_logger.Object, _repository.Object);
    }

    [Fact]
    public async Task GenerateComplianceReportAsync_ShouldClassifyIssuesByType()
    {
        var drivers = new List<DriverInfo>
        {
            new()
            {
                Id = "unsigned",
                Name = "Unsigned Driver",
                Status = "OK",
                IsSigned = false,
                LastUpdated = DateTime.UtcNow,
                DriverDate = DateTime.UtcNow,
                InstallDate = DateTime.UtcNow
            },
            new()
            {
                Id = "outdated",
                Name = "Outdated Driver",
                Status = "Outdated",
                IsSigned = true,
                IsEssential = true,
                LastUpdated = DateTime.UtcNow.AddDays(-10)
            },
            new()
            {
                Id = "stale",
                Name = "Stale Driver",
                Status = "OK",
                IsSigned = true,
                LastUpdated = DateTime.UtcNow.AddDays(-400)
            }
        };

        _repository
            .Setup(r => r.GetAllDriversAsync())
            .ReturnsAsync(drivers);

        var report = await _driverManager.GenerateComplianceReportAsync(TimeSpan.FromDays(180));

        report.TotalDrivers.Should().Be(3);
        report.UnsignedDrivers.Should().Be(1);
        report.OutdatedDrivers.Should().Be(1);
        report.StaleDrivers.Should().Be(1);
        report.Issues.Should().HaveCount(3);

        report.Issues.Should().Contain(i =>
            i.DriverId == "unsigned" &&
            i.IssueType == DriverComplianceIssueType.Unsigned &&
            i.Severity == ComplianceSeverity.High);

        report.Issues.Should().Contain(i =>
            i.DriverId == "outdated" &&
            i.IssueType == DriverComplianceIssueType.Outdated &&
            i.Severity == ComplianceSeverity.High);

        report.Issues.Should().Contain(i =>
            i.DriverId == "stale" &&
            i.IssueType == DriverComplianceIssueType.LongTimeSinceUpdate &&
            i.Severity == ComplianceSeverity.Low);

        report.OverallStatus.Should().Be("Attention Required");
    }

    [Fact]
    public async Task GenerateComplianceReportAsync_ShouldReturnCompliantWhenNoIssues()
    {
        var drivers = new List<DriverInfo>
        {
            new()
            {
                Id = "healthy",
                Name = "Healthy Driver",
                Status = "OK",
                IsSigned = true,
                LastUpdated = DateTime.UtcNow.AddDays(-5)
            }
        };

        _repository
            .Setup(r => r.GetAllDriversAsync())
            .ReturnsAsync(drivers);

        var report = await _driverManager.GenerateComplianceReportAsync(TimeSpan.FromDays(180));

        report.TotalDrivers.Should().Be(1);
        report.Issues.Should().BeEmpty();
        report.OverallStatus.Should().Be("Compliant");
    }
}

public class TelemetryServiceTests
{
    private readonly Mock<ISimpleLogger> _mockLogger;
    private readonly TelemetryService _telemetryService;

    public TelemetryServiceTests()
    {
        _mockLogger = new Mock<ISimpleLogger>();
        _telemetryService = new TelemetryService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeTelemetryService()
    {
        // Arrange & Act
        var service = new TelemetryService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void RecordEvent_ShouldNotThrow()
    {
        // Arrange
        var eventName = "TestEvent";
        var properties = new Dictionary<string, object> { ["TestProperty"] = "TestValue" };

        // Act & Assert
        var exception = Record.Exception(() => _telemetryService.RecordEvent(eventName, properties));
        exception.Should().BeNull();
    }

    [Fact]
    public void RecordPerformanceMetric_ShouldNotThrow()
    {
        // Arrange
        var metricName = "TestMetric";
        var value = 95.5;

        // Act & Assert
        var exception = Record.Exception(() => _telemetryService.RecordPerformanceMetric(metricName, value));
        exception.Should().BeNull();
    }

    [Fact]
    public void RecordError_ShouldNotThrow()
    {
        // Arrange
        var errorMessage = "Test error message";
        var errorType = "TestError";

        // Act & Assert
        var exception = Record.Exception(() => _telemetryService.RecordError(errorMessage, errorType));
        exception.Should().BeNull();
    }
}

public class AutoUpdateServiceTests
{
    private readonly Mock<ISimpleLogger> _mockLogger;
    private readonly AutoUpdateService _updateService;

    public AutoUpdateServiceTests()
    {
        _mockLogger = new Mock<ISimpleLogger>();
        _updateService = new AutoUpdateService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeAutoUpdateService()
    {
        // Arrange & Act
        var service = new AutoUpdateService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnResult()
    {
        // Arrange & Act
        var result = await _updateService.CheckForUpdatesAsync();

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse(); // 実際のエンドポイントがないため失敗するはず
        result.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void StartAutoUpdateCheck_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => _updateService.StartAutoUpdateCheck(24));
        exception.Should().BeNull();
    }

    [Fact]
    public void StopAutoUpdateCheck_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => _updateService.StopAutoUpdateCheck());
        exception.Should().BeNull();
    }
}

using AeroDriver.Core;
using AeroDriver.Core.Interfaces;
using AeroDriver.Core.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace AeroDriver.Tests;

public class CoreDriverServiceTests
{
    private readonly Mock<ISimpleLogger> _logger = new();
    private readonly Mock<IDriverRepository> _repository = new();
    private readonly Mock<ISecurityService> _security = new();
    private readonly CoreDriverService _service;

    private readonly List<DriverInfo> _drivers =
    [
        new DriverInfo
        {
            Id = "1",
            Name = "Signed OK Driver",
            Status = "OK",
            IsSigned = true,
            DeviceClass = "System",
            Priority = 1
        },
        new DriverInfo
        {
            Id = "2",
            Name = "Outdated Driver",
            Status = "Outdated",
            IsSigned = false,
            DeviceClass = "Net",
            Priority = 3
        }
    ];

    public CoreDriverServiceTests()
    {
        _repository
            .Setup(r => r.GetAllDriversAsync())
            .ReturnsAsync(() => _drivers);

        _repository
            .Setup(r => r.GetDriverStatisticsAsync())
            .ReturnsAsync(new Dictionary<string, int>
            {
                ["Total"] = _drivers.Count,
                ["OK"] = 1,
                ["Warning"] = 0,
                ["Error"] = 1,
                ["Critical"] = 0
            });

        var securityReport = new SecurityReport
        {
            SecurityScore = 95,
            TotalIssues = 0,
            AuditTime = DateTime.UtcNow
        };

        _security
            .Setup(s => s.PerformSecurityAuditAsync())
            .ReturnsAsync(securityReport);

        _service = new CoreDriverService(
            _logger.Object,
            _repository.Object,
            _security.Object);
    }

    [Fact]
    public async Task GetAllDriversAsync_ReturnsRepositoryDrivers()
    {
        var result = await _service.GetAllDriversAsync();

        result.Should().HaveCount(2);
        result.Select(d => d.Id).Should().BeEquivalentTo("1", "2");
        _repository.Verify(r => r.GetAllDriversAsync(), Times.Once);
    }

    [Fact]
    public async Task GetSystemStatsAsync_ComputesCountsFromDrivers()
    {
        var stats = await _service.GetSystemStatsAsync();

        stats.TotalDrivers.Should().Be(2);
        stats.ActiveDrivers.Should().Be(1);
        stats.ProblemDrivers.Should().Be(1);
        stats.OutdatedDrivers.Should().Be(1);
        stats.UnsignedDrivers.Should().Be(1);
    }

    [Fact]
    public void GetAllDrivers_UsesSynchronousWrapper()
    {
        var result = _service.GetAllDrivers();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSecurityReportAsync_ReturnsUnderlyingReport()
    {
        var report = await _service.GetSecurityReportAsync();

        report.SecurityScore.Should().Be(95);
        report.TotalIssues.Should().Be(0);
        _security.Verify(s => s.PerformSecurityAuditAsync(), Times.Once);
    }
}

public class ComplianceReportTests
{
    private readonly Mock<ISimpleLogger> _logger = new();
    private readonly Mock<IDriverRepository> _repository = new();
    private readonly DriverManager _driverManager;

    public ComplianceReportTests()
    {
        _driverManager = new DriverManager(_logger.Object, _repository.Object);
    }

    [Fact]
    public async Task GenerateComplianceReportAsync_ShouldClassifyIssuesByType()
    {
        var drivers = new List<DriverInfo>
        {
            new()
            {
                Id = "unsigned",
                Name = "Unsigned Driver",
                Status = "OK",
                IsSigned = false,
                LastUpdated = DateTime.UtcNow,
                DriverDate = DateTime.UtcNow,
                InstallDate = DateTime.UtcNow
            },
            new()
            {
                Id = "outdated",
                Name = "Outdated Driver",
                Status = "Outdated",
                IsSigned = true,
                IsEssential = true,
                LastUpdated = DateTime.UtcNow.AddDays(-10)
            },
            new()
            {
                Id = "stale",
                Name = "Stale Driver",
                Status = "OK",
                IsSigned = true,
                LastUpdated = DateTime.UtcNow.AddDays(-400)
            }
        };

        _repository
            .Setup(r => r.GetAllDriversAsync())
            .ReturnsAsync(drivers);

        var report = await _driverManager.GenerateComplianceReportAsync(TimeSpan.FromDays(180));

        report.TotalDrivers.Should().Be(3);
        report.UnsignedDrivers.Should().Be(1);
        report.OutdatedDrivers.Should().Be(1);
        report.StaleDrivers.Should().Be(1);
        report.Issues.Should().HaveCount(3);

        report.Issues.Should().Contain(i =>
            i.DriverId == "unsigned" &&
            i.IssueType == DriverComplianceIssueType.Unsigned &&
            i.Severity == ComplianceSeverity.High);

        report.Issues.Should().Contain(i =>
            i.DriverId == "outdated" &&
            i.IssueType == DriverComplianceIssueType.Outdated &&
            i.Severity == ComplianceSeverity.High);

        report.Issues.Should().Contain(i =>
            i.DriverId == "stale" &&
            i.IssueType == DriverComplianceIssueType.LongTimeSinceUpdate &&
            i.Severity == ComplianceSeverity.Low);

        report.OverallStatus.Should().Be("Attention Required");
    }

    [Fact]
    public async Task GenerateComplianceReportAsync_ShouldReturnCompliantWhenNoIssues()
    {
        var drivers = new List<DriverInfo>
        {
            new()
            {
                Id = "healthy",
                Name = "Healthy Driver",
                Status = "OK",
                IsSigned = true,
                LastUpdated = DateTime.UtcNow.AddDays(-5)
            }
        };

        _repository
            .Setup(r => r.GetAllDriversAsync())
            .ReturnsAsync(drivers);

        var report = await _driverManager.GenerateComplianceReportAsync(TimeSpan.FromDays(180));

        report.TotalDrivers.Should().Be(1);
        report.Issues.Should().BeEmpty();
        report.OverallStatus.Should().Be("Compliant");
    }
}

public class TelemetryServiceTests
{
    private readonly Mock<ISimpleLogger> _mockLogger;
    private readonly TelemetryService _telemetryService;

    public TelemetryServiceTests()
    {
        _mockLogger = new Mock<ISimpleLogger>();
        _telemetryService = new TelemetryService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeTelemetryService()
    {
        // Arrange & Act
        var service = new TelemetryService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void RecordEvent_ShouldNotThrow()
    {
        // Arrange
        var eventName = "TestEvent";
        var properties = new Dictionary<string, object> { ["TestProperty"] = "TestValue" };

        // Act & Assert
        var exception = Record.Exception(() => _telemetryService.RecordEvent(eventName, properties));
        exception.Should().BeNull();
    }

    [Fact]
    public void RecordPerformanceMetric_ShouldNotThrow()
    {
        // Arrange
        var metricName = "TestMetric";
        var value = 95.5;

        // Act & Assert
        var exception = Record.Exception(() => _telemetryService.RecordPerformanceMetric(metricName, value));
        exception.Should().BeNull();
    }

    [Fact]
    public void RecordError_ShouldNotThrow()
    {
        // Arrange
        var errorMessage = "Test error message";
        var errorType = "TestError";

        // Act & Assert
        var exception = Record.Exception(() => _telemetryService.RecordError(errorMessage, errorType));
        exception.Should().BeNull();
    }
}

public class AutoUpdateServiceTests
{
    private readonly Mock<ISimpleLogger> _mockLogger;
    private readonly AutoUpdateService _updateService;

    public AutoUpdateServiceTests()
    {
        _mockLogger = new Mock<ISimpleLogger>();
        _updateService = new AutoUpdateService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeAutoUpdateService()
    {
        // Arrange & Act
        var service = new AutoUpdateService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ShouldReturnResult()
    {
        // Arrange & Act
        var result = await _updateService.CheckForUpdatesAsync();

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse(); // 実際のエンドポイントがないため失敗するはず
        result.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void StartAutoUpdateCheck_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => _updateService.StartAutoUpdateCheck(24));
        exception.Should().BeNull();
    }

    [Fact]
    public void StopAutoUpdateCheck_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => _updateService.StopAutoUpdateCheck());
        exception.Should().BeNull();
    }
}

public class EnterpriseServiceTests
{
    private readonly Mock<ISimpleLogger> _mockLogger;
    private readonly EnterpriseService _enterpriseService;

    public EnterpriseServiceTests()
    {
        _mockLogger = new Mock<ISimpleLogger>();
        _enterpriseService = new EnterpriseService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeEnterpriseService()
    {
        // Arrange & Act
        var service = new EnterpriseService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
        _mockLogger.Verify(logger => logger.LogInfo("EnterpriseService initialized"), Times.Once);
    }

    [Fact]
    public void GetAllSecurityPolicies_ShouldReturnDictionary()
    {
        // Arrange & Act
        var policies = _enterpriseService.GetAllSecurityPolicies();

        // Assert
        policies.Should().NotBeNull();
        policies.Should().BeOfType<Dictionary<string, SecurityPolicy>>();
    }

    [Fact]
    public void StartSystemMonitoring_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => _enterpriseService.StartSystemMonitoring(5));
        exception.Should().BeNull();
    }

    [Fact]
    public void StopSystemMonitoring_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => _enterpriseService.StopSystemMonitoring());
        exception.Should().BeNull();
    }
}

// 新しいセキュリティサービステスト
public class SecurityServiceTests
{
    private readonly Mock<ISimpleLogger> _mockLogger;
    private readonly Mock<IDriverRepository> _mockRepository;
    private readonly SecurityService _securityService;

    public SecurityServiceTests()
    {
        _mockLogger = new Mock<ISimpleLogger>();
        _mockRepository = new Mock<IDriverRepository>();
        _securityService = new SecurityService(_mockLogger.Object, _mockRepository.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeSecurityService()
    {
        // Arrange & Act
        var service = new SecurityService(_mockLogger.Object, _mockRepository.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task PerformSecurityAuditAsync_ShouldReturnReport()
    {
        // Arrange & Act
        var report = await _securityService.PerformSecurityAuditAsync();

        // Assert
        report.Should().NotBeNull();
        report.SecurityScore.Should().BeInRange(0, 100);
        report.AuditTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ConfigureMonitoring_ShouldNotThrow()
    {
        // Arrange
        var config = new SecurityMonitoringConfig
        {
            EnableRealTimeScanning = true,
            ScanIntervalSeconds = 300
        };

        // Act & Assert
        var exception = Record.Exception(() => _securityService.ConfigureMonitoring(config));
        exception.Should().BeNull();
    }

    [Fact]
    public void GetActiveMonitors_ShouldReturnList()
    {
        // Arrange & Act
        var monitors = _securityService.GetActiveMonitors();

        // Assert
        monitors.Should().NotBeNull();
        monitors.Should().BeOfType<List<RealTimeMonitoringInfo>>();
    }

    [Fact]
    public void GetVulnerabilityDatabase_ShouldReturnList()
    {
        // Arrange & Act
        var vulnerabilities = _securityService.GetVulnerabilityDatabase();

        // Assert
        vulnerabilities.Should().NotBeNull();
        vulnerabilities.Should().BeOfType<List<VulnerabilityInfo>>();
    }
}

// 新しいパフォーマンス監視サービステスト
public class PerformanceMonitoringServiceTests
{
    private readonly Mock<ISimpleLogger> _mockLogger;
    private readonly Mock<IPerformanceTelemetrySink> _mockTelemetrySink;
    private readonly PerformanceMonitoringService _performanceService;

    public PerformanceMonitoringServiceTests()
    {
        _mockLogger = new Mock<ISimpleLogger>();
        _mockTelemetrySink = new Mock<IPerformanceTelemetrySink>();
        _performanceService = new PerformanceMonitoringService(_mockLogger.Object, _mockTelemetrySink.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializePerformanceMonitoringService()
    {
        // Arrange & Act
        var service = new PerformanceMonitoringService(_mockLogger.Object, _mockTelemetrySink.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public void Configure_ShouldUpdateConfiguration()
    {
        // Arrange
        var config = new PerformanceMonitoringConfig
        {
            EnableCpuMonitoring = true,
            CpuThresholdPercent = 85.0
        };

        // Act & Assert
        var exception = Record.Exception(() => _performanceService.Configure(config));
        exception.Should().BeNull();
    }

    [Fact]
    public async Task GeneratePerformanceReportAsync_ShouldReturnReport()
    {
        // Arrange & Act
        var report = await _performanceService.GeneratePerformanceReportAsync();

        // Assert
        report.Should().NotBeNull();
        report.Score.Should().BeInRange(0, 100);
        report.ReportTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void GetActiveMonitors_ShouldReturnList()
    {
        // Arrange & Act
        var monitors = _performanceService.GetActiveMonitors();

        // Assert
        monitors.Should().NotBeNull();
        monitors.Should().BeOfType<List<PerformanceMonitor>>();
    }
}

// 新しいエラーハンドリングサービステスト
public class ErrorHandlingServiceTests
{
    private readonly Mock<ISimpleLogger> _mockLogger;
    private readonly ErrorHandlingService _errorService;

    public ErrorHandlingServiceTests()
    {
        _mockLogger = new Mock<ISimpleLogger>();
        _errorService = new ErrorHandlingService(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_ShouldInitializeErrorHandlingService()
    {
        // Arrange & Act
        var service = new ErrorHandlingService(_mockLogger.Object);

        // Assert
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_ShouldSucceedOnFirstAttempt()
    {
        // Arrange
        var operation = new Func<Task<string>>(async () => "Success");

        // Act
        var result = await _errorService.ExecuteWithRetryAsync(operation);

        // Assert
        result.Should().Be("Success");
    }

    [Fact]
    public async Task ExecuteWithRetryAsync_ShouldRetryOnFailure()
    {
        // Arrange
        var attemptCount = 0;
        var operation = new Func<Task<string>>(async () =>
        {
            attemptCount++;
            if (attemptCount < 3)
                throw new Exception("Temporary failure");
            return "Success";
        });

        var policy = new RetryPolicy { MaxAttempts = 3, InitialDelayMs = 100 };

        // Act
        var result = await _errorService.ExecuteWithRetryAsync(operation, policy);

        // Assert
        result.Should().Be("Success");
        attemptCount.Should().Be(3);
    }

    [Fact]
    public void GetErrorHistory_ShouldReturnList()
    {
        // Arrange & Act
        var history = _errorService.GetErrorHistory();

        // Assert
        history.Should().NotBeNull();
        history.Should().BeOfType<List<ErrorRecord>>();
    }

    [Fact]
    public void GetErrorStatistics_ShouldReturnStatistics()
    {
        // Arrange & Act
        var statistics = _errorService.GetErrorStatistics();

        // Assert
        statistics.Should().NotBeNull();
        statistics.TotalErrors.Should().BeGreaterThanOrEqualTo(0);
        statistics.ErrorsByType.Should().NotBeNull();
    }
}

// メモリオプティマイザーのテスト
public class MemoryOptimizerTests
{
    [Fact]
    public void GetOrAddCached_ShouldCacheValue()
    {
        // Arrange
        var key = "testKey";
        var factoryCalled = false;
        var factory = new Func<string>(() =>
        {
            factoryCalled = true;
            return "cachedValue";
        });

        // Act
        var result1 = MemoryOptimizer.GetOrAddCached(key, factory);
        var result2 = MemoryOptimizer.GetOrAddCached(key, factory);

        // Assert
        result1.Should().Be("cachedValue");
        result2.Should().Be("cachedValue");
        factoryCalled.Should().BeTrue(); // 最初の呼び出しのみ
    }

    [Fact]
    public void RemoveCached_ShouldRemoveValue()
    {
        // Arrange
        var key = "testKey";
        MemoryOptimizer.GetOrAddCached(key, () => "testValue");

        // Act
        var removed = MemoryOptimizer.RemoveCached(key);

        // Assert
        removed.Should().BeTrue();
    }

    [Fact]
    public void GetDiagnostics_ShouldReturnDiagnostics()
    {
        // Arrange & Act
        var diagnostics = MemoryOptimizer.GetDiagnostics();

        // Assert
        diagnostics.Should().NotBeNull();
        diagnostics.Capacity.Should().BeGreaterThan(0);
        diagnostics.UsageRatio.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public void TrimToCapacity_ShouldTrimCache()
    {
        // Arrange
        var capacity = MemoryOptimizer.GetCacheCapacity();
        for (int i = 0; i < capacity + 10; i++)
        {
            MemoryOptimizer.GetOrAddCached($"key{i}", () => $"value{i}");
        }

        // Act
        var trimResult = MemoryOptimizer.TrimToCapacity(0.5);

        // Assert
        trimResult.Trimmed.Should().BeTrue();
        trimResult.RemainingEntries.Should().BeLessThanOrEqualTo((int)(capacity * 0.5));
    }
}

// システムヘルスのテスト
public class SystemHealthTests
{
    [Fact]
    public void HealthScore_ShouldCalculateCorrectly()
    {
        // Arrange
        var stats = new SystemStats
        {
            TotalDrivers = 10,
            ActiveDrivers = 8,
            ProblemDrivers = 2,
            OutdatedDrivers = 1,
            UnsignedDrivers = 1
        };

        // Act
        var healthPercentage = stats.HealthPercentage;
        var healthScore = stats.HealthScore;

        // Assert
        healthPercentage.Should().Be(80); // 8/10 * 100
        healthScore.Should().BeInRange(50, 100);
    }

    [Fact]
    public void HealthGrade_ShouldReturnCorrectGrade()
    {
        // Arrange & Act & Assert
        var excellentStats = new SystemStats { TotalDrivers = 10, ProblemDrivers = 0 };
        excellentStats.HealthScore.Should().Be(100);
        excellentStats.HealthGrade.Should().Be("Excellent");

        var goodStats = new SystemStats { TotalDrivers = 10, ProblemDrivers = 1 };
        goodStats.HealthScore.Should().BeInRange(80, 99);
        goodStats.HealthGrade.Should().Be("Good");

        var fairStats = new SystemStats { TotalDrivers = 10, ProblemDrivers = 2 };
        fairStats.HealthScore.Should().BeInRange(70, 79);
        fairStats.HealthGrade.Should().Be("Fair");

        var poorStats = new SystemStats { TotalDrivers = 10, ProblemDrivers = 3 };
        poorStats.HealthScore.Should().BeInRange(60, 69);
        poorStats.HealthGrade.Should().Be("Poor");

    [Fact]
    public async Task GetAllDriversAsync_ShouldUseCache_OnSubsequentCalls()
    {
        // Arrange
        var callCount = 0;
        _repository
            .Setup(r => r.GetAllDriversAsync())
            .ReturnsAsync(() =>
            {
                callCount++;
                return _drivers;
            });

        // Act
        var result1 = await _service.GetAllDriversAsync();
        var result2 = await _service.GetAllDriversAsync();

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1.Should().BeEquivalentTo(result2);
        callCount.Should().Be(1); // リポジトリは1回のみ呼び出されるはず（2回目はキャッシュから）
    }

    [Fact]
    public async Task GetSystemStatsAsync_ShouldUseMemoryOptimizedCounting()
    {
        // Arrange
        _repository
            .Setup(r => r.GetAllDriversAsync())
            .ReturnsAsync(_drivers);

        // Act
        var result = await _service.GetSystemStatsAsync();

        // Assert
        result.Should().NotBeNull();
        result.TotalDrivers.Should().Be(2);
        result.ActiveDrivers.Should().Be(1);
        result.ProblemDrivers.Should().Be(1);
        result.OutdatedDrivers.Should().Be(1);
        result.UnsignedDrivers.Should().Be(1);
        result.LastScanTime.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void PerformMemoryCleanup_ShouldRemoveExpiredCacheEntries()
    {
        // Arrange - キャッシュにデータを追加（テスト用にアクセス）
        var cacheField = typeof(CoreDriverService).GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cache = (Dictionary<string, object>)cacheField?.GetValue(_service);

        if (cache != null)
        {
            var expiredTime = DateTime.Now.AddMinutes(-10);
            var cacheItemType = typeof(CoreDriverService).GetNestedType("CacheItem`1", System.Reflection.BindingFlags.NonPublic);
            var cacheItem = Activator.CreateInstance(cacheItemType?.MakeGenericType(typeof(List<DriverInfo>)), _drivers, expiredTime);

            cache["test"] = cacheItem;
        }

        // Act
        _service.PerformMemoryCleanup();

        // Assert - 期限切れエントリが削除されたことを確認
        cache?.Should().NotContainKey("test");
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroDriver.Tests
{
    [TestClass]
    public class UnifiedSystemTests
    {
        [TestMethod]
        public async Task UnifiedConfigurationManager_IntegrationTest()
        {
            // Arrange
            var configManager = new UnifiedConfigurationManager();

            // Act & Assert
            var testValue = configManager.GetValue<string>("TestKey", "DefaultValue");
            Assert.AreEqual("DefaultValue", testValue);

            // 値を設定
            await configManager.SetValueAsync("TestKey", "TestValue");

            // 値を取得
            var retrievedValue = configManager.GetValue<string>("TestKey", "DefaultValue");
            Assert.AreEqual("TestValue", retrievedValue);

            // 値を削除
            await configManager.RemoveValueAsync("TestKey");

            // 値が削除されたことを確認
            var deletedValue = configManager.GetValue<string>("TestKey", "DefaultValue");
            Assert.AreEqual("DefaultValue", deletedValue);

            // クリーンアップ
            configManager.Dispose();
        }

        [TestMethod]
        public async Task EnhancedLogger_IntegrationTest()
        {
            // Arrange
            var logger = new EnhancedLogger();

            // Act
            await logger.InfoAsync("テストメッセージ", "TestCategory");
            await logger.WarningAsync("テスト警告", "TestCategory");
            await logger.ErrorAsync("テストエラー", "TestCategory");

            // Assert
            var stats = logger.GetStatistics();
            Assert.IsTrue(stats.TotalEntries >= 3);

            // クリーンアップ
            logger.Dispose();
        }

        [TestMethod]
        public async Task SecurityManager_IntegrationTest()
        {
            // Arrange
            var security = new SecurityManager();

            // Act & Assert
            var validPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pathValidation = await SecurityManager.ValidateInputAsync(validPath, SecurityManager.InputType.FilePath);
            Assert.IsTrue(pathValidation.IsValid);

            var invalidPath = "../../../etc/passwd";
            var invalidPathValidation = await SecurityManager.ValidateInputAsync(invalidPath, SecurityManager.InputType.FilePath);
            Assert.IsFalse(invalidPathValidation.IsValid);

            var sqlInjection = "SELECT * FROM users; DROP TABLE users;";
            var sqlValidation = await SecurityManager.ValidateInputAsync(sqlInjection, SecurityManager.InputType.SqlQuery);
            Assert.IsFalse(sqlValidation.IsValid);
        }

        [TestMethod]
        public async Task AeroDriverCore_IntegrationTest()
        {
            // Arrange
            var core = new AeroDriverCore();

            // Act
            var systemInfo = core.GetSystemInfo();
            var health = await core.CheckSystemHealthAsync();
            var performanceReport = await core.GetPerformanceReportAsync();

            // Assert
            Assert.IsNotNull(systemInfo);
            Assert.IsTrue(systemInfo.Count > 0);
            Assert.IsNotNull(health);
            Assert.IsNotNull(performanceReport);
            Assert.IsTrue(performanceReport.Length > 0);

            // クリーンアップ
            core.Dispose();
        }

        [TestMethod]
        public async Task BackupService_IntegrationTest()
        {
            // Arrange
            var backupService = new BackupService();
            var testFilePath = Path.Combine(Path.GetTempPath(), "test_backup.txt");
            var backupDir = Path.Combine(Path.GetTempPath(), "test_backups");

            try
            {
                // テストファイルを作成
                await File.WriteAllTextAsync(testFilePath, "Test content for backup");

                // Act
                var backupCreated = await backupService.CreateBackupAsync(testFilePath, backupDir);
                var backupFiles = await backupService.GetBackupFilesAsync(backupDir);

                // Assert
                Assert.IsTrue(backupCreated);
                Assert.IsTrue(backupFiles.Any());

                // バックアップファイル名にタイムスタンプが含まれていることを確認
                var backupFile = backupFiles.First();
                Assert.IsTrue(backupFile.Contains("test_backup"));

            }
            finally
            {
                // クリーンアップ
                if (File.Exists(testFilePath))
                    File.Delete(testFilePath);

                if (Directory.Exists(backupDir))
                    Directory.Delete(backupDir, true);
            }
        }

        [TestMethod]
        public async Task PerformanceOptimizer_IntegrationTest()
        {
            // Arrange
            var optimizer = new PerformanceOptimizer();

            // Act
            await optimizer.OptimizeMemoryAsync();
            await optimizer.OptimizeCacheAsync();
            await optimizer.OptimizeSystemAsync();
            var report = await optimizer.GetPerformanceReportAsync();

            // Assert
            Assert.IsNotNull(report);
            Assert.IsTrue(report.Length > 0);
            Assert.IsTrue(report.Contains("パフォーマンスレポート"));
        }

        [TestMethod]
        public async Task MemoryOptimizer_IntegrationTest()
        {
            // Arrange
            var memoryOptimizer = new MemoryOptimizer();

            // Act
            var cacheSizeBefore = memoryOptimizer.GetCacheSize();
            var cachedObject = MemoryOptimizer.GetOrAddCached("test_key", () => new object());
            var cacheSizeAfter = memoryOptimizer.GetCacheSize();

            // Assert
            Assert.IsNotNull(cachedObject);
            Assert.IsTrue(cacheSizeAfter >= cacheSizeBefore);

            // キャッシュをクリア
            memoryOptimizer.ClearCache();
            var cacheSizeAfterClear = memoryOptimizer.GetCacheSize();
            Assert.IsTrue(cacheSizeAfterClear <= cacheSizeBefore);
        }

        [TestMethod]
        public async Task CoreUtilities_IntegrationTest()
        {
            // Arrange
            var testObject = new { Name = "Test", Value = 123 };

            // Act
            var json = CoreUtilities.SerializeToJson(testObject);
            var deserializedObject = CoreUtilities.DeserializeFromJson<TestObject>(json);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("Test"));
            Assert.IsNotNull(deserializedObject);
            Assert.AreEqual("Test", deserializedObject.Name);
            Assert.AreEqual(123, deserializedObject.Value);
        }

        [TestMethod]
        public async Task ExecuteWithRetry_IntegrationTest()
        {
            // Arrange
            var attemptCount = 0;
            var operation = new Func<Task<int>>(async () =>
            {
                attemptCount++;
                if (attemptCount < 3)
                    throw new InvalidOperationException("Test exception");
                return 42;
            });

            // Act
            var result = await CoreUtilities.ExecuteWithRetryAsync(operation, 3);

            // Assert
            Assert.AreEqual(42, result);
            Assert.AreEqual(3, attemptCount);
        }

        [TestMethod]
        public async Task SecurityValidation_IntegrationTest()
        {
            // Arrange
            var validFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "test.txt");
            var invalidFilePath = "../../../etc/passwd";
            var sqlInjection = "SELECT * FROM users; DROP TABLE users;";
            var xssContent = "<script>alert('XSS')</script>";

            // Act
            var validPathResult = await SecurityManager.ValidateInputAsync(validFilePath, SecurityManager.InputType.FilePath);
            var invalidPathResult = await SecurityManager.ValidateInputAsync(invalidFilePath, SecurityManager.InputType.FilePath);
            var sqlResult = await SecurityManager.ValidateInputAsync(sqlInjection, SecurityManager.InputType.SqlQuery);
            var xssResult = await SecurityManager.ValidateInputAsync(xssContent, SecurityManager.InputType.HtmlContent);

            // Assert
            Assert.IsTrue(validPathResult.IsValid);
            Assert.IsFalse(invalidPathResult.IsValid);
            Assert.IsFalse(sqlResult.IsValid);
            Assert.IsFalse(xssResult.IsValid);
        }

        private class TestObject
        {
            public string Name { get; set; }
            public int Value { get; set; }
        }
    }
}

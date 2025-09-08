using System;
using System.IO;
using AeroDriver.Core.Services;
using Xunit;

namespace AeroDriver.Core.Tests
{
    public class SettingsServiceTests : IDisposable
    {
        private readonly string _testSettingsPath;

        public SettingsServiceTests()
        {
            // Create temporary test directory
            var tempPath = Path.GetTempPath();
            var testDir = Path.Combine(tempPath, "AeroDriverTest", Guid.NewGuid().ToString());
            Directory.CreateDirectory(testDir);
            _testSettingsPath = testDir;
            
            // Temporarily set the AppData path for testing
            Environment.SetEnvironmentVariable("APPDATA", tempPath);
        }

        [Fact]
        public void Constructor_CreatesDefaultSettings()
        {
            // Act
            var settingsService = new SettingsService();

            // Assert
            Assert.False(settingsService.AutoUpdateEnabled);
            Assert.False(settingsService.IncludeBetaDrivers);
            Assert.True(settingsService.BackupEnabled);
            Assert.Equal(3, settingsService.MaxBackupGenerations);
        }

        [Fact]
        public void Save_PersistsSettings()
        {
            // Arrange
            var settingsService = new SettingsService();
            settingsService.AutoUpdateEnabled = true;
            settingsService.IncludeBetaDrivers = true;
            settingsService.BackupEnabled = false;
            settingsService.MaxBackupGenerations = 5;

            // Act
            settingsService.Save();

            // Create new instance to test persistence
            var newSettingsService = new SettingsService();

            // Assert
            Assert.True(newSettingsService.AutoUpdateEnabled);
            Assert.True(newSettingsService.IncludeBetaDrivers);
            Assert.False(newSettingsService.BackupEnabled);
            Assert.Equal(5, newSettingsService.MaxBackupGenerations);
        }

        [Fact]
        public void ResetToDefaults_RestoresDefaultValues()
        {
            // Arrange
            var settingsService = new SettingsService();
            settingsService.AutoUpdateEnabled = true;
            settingsService.IncludeBetaDrivers = true;
            settingsService.BackupEnabled = false;
            settingsService.MaxBackupGenerations = 10;

            // Act
            settingsService.ResetToDefaults();

            // Assert
            Assert.False(settingsService.AutoUpdateEnabled);
            Assert.False(settingsService.IncludeBetaDrivers);
            Assert.True(settingsService.BackupEnabled);
            Assert.Equal(3, settingsService.MaxBackupGenerations);
        }

        [Fact]
        public void Settings_ModifyAndSave_PersistsChanges()
        {
            // Arrange
            var settingsService = new SettingsService();

            // Act - Modify settings
            settingsService.AutoUpdateEnabled = true;
            settingsService.MaxBackupGenerations = 7;
            settingsService.Save();

            // Create new instance and verify
            var verifyService = new SettingsService();

            // Assert
            Assert.True(verifyService.AutoUpdateEnabled);
            Assert.Equal(7, verifyService.MaxBackupGenerations);
            Assert.True(verifyService.BackupEnabled); // Should remain default
            Assert.False(verifyService.IncludeBetaDrivers); // Should remain default
        }

        public void Dispose()
        {
            // Clean up test directory
            try
            {
                if (Directory.Exists(_testSettingsPath))
                {
                    Directory.Delete(_testSettingsPath, true);
                }
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}
using System;
using System.IO;
using System.Threading.Tasks;
using AeroDriver.Core.Helpers;
using Xunit;

namespace AeroDriver.Core.Tests
{
    public class FileHelperTests : IDisposable
    {
        private readonly List<string> _tempFiles = new();
        private readonly List<string> _tempDirectories = new();

        [Fact]
        public void GetHumanReadableSize_ReturnsCorrectFormat()
        {
            // Test various sizes
            Assert.Equal("0 B", FileHelper.GetHumanReadableSize(0));
            Assert.Equal("1 B", FileHelper.GetHumanReadableSize(1));
            Assert.Equal("1 KB", FileHelper.GetHumanReadableSize(1024));
            Assert.Equal("1 MB", FileHelper.GetHumanReadableSize(1024 * 1024));
            Assert.Equal("1.5 KB", FileHelper.GetHumanReadableSize(1536));
        }

        [Fact]
        public void HasEnoughSpace_ReturnsTrueForSmallFiles()
        {
            // Small file requirement should always pass
            var result = FileHelper.HasEnoughSpace(@"C:\", 1000);
            Assert.True(result);
        }

        [Fact]
        public void IsFileLocked_ReturnsFalseForNonExistentFile()
        {
            var nonExistentFile = Path.GetRandomFileName();
            var result = FileHelper.IsFileLocked(nonExistentFile);
            Assert.False(result);
        }

        [Fact]
        public async Task CalculateHashAsync_ReturnsSameHashForSameContent()
        {
            // Create temporary file with known content
            var tempFile = CreateTempFile();
            await File.WriteAllTextAsync(tempFile, "test content");

            var hash1 = await FileHelper.CalculateHashAsync(tempFile);
            var hash2 = await FileHelper.CalculateHashAsync(tempFile);

            Assert.Equal(hash1, hash2);
            Assert.NotNull(hash1);
            Assert.NotEmpty(hash1);
        }

        [Fact]
        public async Task SafeCopyAsync_SucceedsWithValidFiles()
        {
            var sourceFile = CreateTempFile();
            var destFile = CreateTempFile();
            
            await File.WriteAllTextAsync(sourceFile, "test content");
            
            var result = await FileHelper.SafeCopyAsync(sourceFile, destFile, true);
            
            Assert.True(result);
            Assert.True(File.Exists(destFile));
            
            var sourceContent = await File.ReadAllTextAsync(sourceFile);
            var destContent = await File.ReadAllTextAsync(destFile);
            Assert.Equal(sourceContent, destContent);
        }

        [Fact]
        public void CreateTempFile_ReturnsValidPath()
        {
            var tempFile = FileHelper.CreateTempFile(".txt");
            
            Assert.NotNull(tempFile);
            Assert.Contains("AeroDriver", tempFile);
            Assert.EndsWith(".txt", tempFile);
            
            // Clean up directory
            var dir = Path.GetDirectoryName(tempFile);
            if (Directory.Exists(dir))
            {
                _tempDirectories.Add(dir);
            }
        }

        [Fact]
        public void CleanupOldFiles_ReturnsZeroForNonExistentDirectory()
        {
            var nonExistentDir = Path.GetRandomFileName();
            var result = FileHelper.CleanupOldFiles(nonExistentDir, TimeSpan.FromDays(1));
            Assert.Equal(0, result);
        }

        [Fact]
        public async Task CreateBackupAsync_ReturnsNullForNonExistentFile()
        {
            var nonExistentFile = Path.GetRandomFileName();
            var result = await FileHelper.CreateBackupAsync(nonExistentFile);
            Assert.Null(result);
        }

        private string CreateTempFile()
        {
            var tempFile = Path.GetTempFileName();
            _tempFiles.Add(tempFile);
            return tempFile;
        }

        public void Dispose()
        {
            // Clean up temporary files
            foreach (var file in _tempFiles)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                }
                catch { /* Ignore cleanup errors */ }
            }

            // Clean up temporary directories
            foreach (var dir in _tempDirectories)
            {
                try
                {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }
}
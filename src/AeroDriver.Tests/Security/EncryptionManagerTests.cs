using System;
using System.Security;
using System.Threading.Tasks;
using AeroDriver.Core.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AeroDriver.Tests.Security
{
    [TestClass]
    public class EncryptionManagerTests
    {
        private const string TestPassword = "TestPassword123!@#";
        private const string TestPlainText = "This is a test message for encryption.";

        [TestMethod]
        public async Task EncryptStringAsync_ValidInput_ReturnsEncryptedString()
        {
            // Arrange & Act
            var encrypted = await EncryptionManager.EncryptStringAsync(TestPlainText, TestPassword);

            // Assert
            Assert.IsNotNull(encrypted);
            Assert.IsFalse(string.IsNullOrEmpty(encrypted));
            Assert.AreNotEqual(TestPlainText, encrypted);
        }

        [TestMethod]
        public async Task DecryptStringAsync_ValidEncryptedString_ReturnsOriginalText()
        {
            // Arrange
            var encrypted = await EncryptionManager.EncryptStringAsync(TestPlainText, TestPassword);

            // Act
            var decrypted = await EncryptionManager.DecryptStringAsync(encrypted, TestPassword);

            // Assert
            Assert.AreEqual(TestPlainText, decrypted);
        }

        [TestMethod]
        public async Task DecryptStringAsync_WrongPassword_ThrowsSecurityException()
        {
            // Arrange
            var encrypted = await EncryptionManager.EncryptStringAsync(TestPlainText, TestPassword);
            var wrongPassword = "WrongPassword123!@#";

            // Act & Assert
            await Assert.ThrowsExceptionAsync<SecurityException>(() =>
                EncryptionManager.DecryptStringAsync(encrypted, wrongPassword));
        }

        [TestMethod]
        public async Task EncryptStringAsync_EmptyText_ReturnsEmptyString()
        {
            // Arrange & Act
            var encrypted = await EncryptionManager.EncryptStringAsync(string.Empty, TestPassword);

            // Assert
            Assert.AreEqual(string.Empty, encrypted);
        }

        [TestMethod]
        public async Task DecryptStringAsync_EmptyText_ReturnsEmptyString()
        {
            // Arrange & Act
            var decrypted = await EncryptionManager.DecryptStringAsync(string.Empty, TestPassword);

            // Assert
            Assert.AreEqual(string.Empty, decrypted);
        }

        [TestMethod]
        public void EncryptStringAsync_NullPassword_ThrowsArgumentException()
        {
            // Arrange & Act & Assert
            Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                EncryptionManager.EncryptStringAsync(TestPlainText, null!));
        }

        [TestMethod]
        public void DecryptStringAsync_NullPassword_ThrowsArgumentException()
        {
            // Arrange & Act & Assert
            Assert.ThrowsExceptionAsync<ArgumentException>(() =>
                EncryptionManager.DecryptStringAsync("encrypted", null!));
        }

        [TestMethod]
        public async Task IsValidEncryptedString_ValidEncryptedString_ReturnsTrue()
        {
            // Arrange
            var encrypted = await EncryptionManager.EncryptStringAsync(TestPlainText, TestPassword);

            // Act
            var isValid = EncryptionManager.IsValidEncryptedString(encrypted);

            // Assert
            Assert.IsTrue(isValid);
        }

        [TestMethod]
        public void IsValidEncryptedString_InvalidBase64_ReturnsFalse()
        {
            // Arrange
            var invalidBase64 = "This is not valid base64!@#$%";

            // Act
            var isValid = EncryptionManager.IsValidEncryptedString(invalidBase64);

            // Assert
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public void IsValidEncryptedString_EmptyString_ReturnsFalse()
        {
            // Arrange & Act
            var isValid = EncryptionManager.IsValidEncryptedString(string.Empty);

            // Assert
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public async Task ComputeHashAsync_ValidData_ReturnsHash()
        {
            // Arrange
            var data = "Test data for hashing";

            // Act
            var hash = await EncryptionManager.ComputeHashAsync(data);

            // Assert
            Assert.IsNotNull(hash);
            Assert.IsFalse(string.IsNullOrEmpty(hash));
        }

        [TestMethod]
        public async Task ComputeHashAsync_SameData_ReturnsSameHash()
        {
            // Arrange
            var data = "Test data for hashing";

            // Act
            var hash1 = await EncryptionManager.ComputeHashAsync(data);
            var hash2 = await EncryptionManager.ComputeHashAsync(data);

            // Assert
            Assert.AreEqual(hash1, hash2);
        }

        [TestMethod]
        public async Task ComputeHashAsync_DifferentData_ReturnsDifferentHash()
        {
            // Arrange
            var data1 = "Test data for hashing";
            var data2 = "Different test data for hashing";

            // Act
            var hash1 = await EncryptionManager.ComputeHashAsync(data1);
            var hash2 = await EncryptionManager.ComputeHashAsync(data2);

            // Assert
            Assert.AreNotEqual(hash1, hash2);
        }
    }
}

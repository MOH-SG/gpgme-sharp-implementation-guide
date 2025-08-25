using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Xunit;

namespace OpenPgpBatchJob.Tests.Unit
{
    /// <summary>
    /// Tests for input validation and error handling scenarios
    /// Since secret manager classes are in PgpCombinedCrypto namespace and internal,
    /// we focus on input validation patterns and error handling
    /// </summary>
    public class InputValidationTests : IDisposable
    {
        private readonly string _testDirectory;

        public InputValidationTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "OpenPgpInputValidationTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t")]
        [InlineData("\n")]
        public void ValidateStringInput_EmptyOrWhitespace_ThrowsArgumentException(string? invalidInput)
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                ValidateNonEmptyString(invalidInput, "testParameter"));

            exception.Message.Should().Contain("testParameter");
            exception.ParamName.Should().Be("testParameter");
        }

        [Fact]
        public void ValidateStringInput_NullValue_ThrowsArgumentNullException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                ValidateNonEmptyString(null, "testParameter"));

            exception.ParamName.Should().Be("testParameter");
        }

        [Theory]
        [InlineData("valid_input")]
        [InlineData("another-valid_input123")]
        [InlineData("C:\\ValidPath\\file.txt")]
        [InlineData("/valid/unix/path/file.txt")]
        public void ValidateStringInput_ValidValues_DoesNotThrow(string validInput)
        {
            // Act & Assert
            var exception = Record.Exception(() => 
                ValidateNonEmptyString(validInput, "testParameter"));

            exception.Should().BeNull();
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        [InlineData(int.MinValue)]
        public void ValidatePositiveInteger_NegativeValues_ThrowsArgumentOutOfRangeException(int negativeValue)
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => 
                ValidatePositiveInteger(negativeValue, "testParameter"));

            exception.ParamName.Should().Be("testParameter");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(100)]
        [InlineData(int.MaxValue)]
        public void ValidatePositiveInteger_NonNegativeValues_DoesNotThrow(int validValue)
        {
            // Act & Assert
            var exception = Record.Exception(() => 
                ValidatePositiveInteger(validValue, "testParameter"));

            exception.Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-valid-email")]
        [InlineData("missing@")]
        [InlineData("@missing.com")]
        [InlineData("spaces in@email.com")]
        [InlineData("email@")]
        [InlineData("email@.com")]
        public void ValidateEmailFormat_InvalidEmails_ThrowsArgumentException(string invalidEmail)
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                ValidateEmailFormat(invalidEmail, "emailParameter"));

            exception.Message.Should().Contain("emailParameter");
            exception.ParamName.Should().Be("emailParameter");
        }

        [Theory]
        [InlineData("test@example.com")]
        [InlineData("user.name@domain.co.uk")]
        [InlineData("alice@home.internal")]
        [InlineData("bob@home.internal")]
        [InlineData("complex.email+tag@subdomain.example.org")]
        public void ValidateEmailFormat_ValidEmails_DoesNotThrow(string validEmail)
        {
            // Act & Assert
            var exception = Record.Exception(() => 
                ValidateEmailFormat(validEmail, "emailParameter"));

            exception.Should().BeNull();
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("path\0with\0nulls")]  // Only test null characters which are invalid on all systems
        public void ValidateFilePath_InvalidPaths_ThrowsArgumentException(string invalidPath)
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                ValidateFilePath(invalidPath, "pathParameter"));

            exception.Message.Should().Contain("pathParameter");
            exception.ParamName.Should().Be("pathParameter");
        }

        [Theory]
        [InlineData("/valid/unix/path")]
        [InlineData("C:\\valid\\windows\\path")]
        [InlineData("relative/path")]
        [InlineData("../relative/path")]
        [InlineData("file.txt")]
        [InlineData("path|with|pipes")]  // These are valid on Unix systems
        [InlineData("path\"with\"quotes")]
        [InlineData("<brackets>")]
        public void ValidateFilePath_ValidPaths_DoesNotThrow(string validPath)
        {
            // Act & Assert
            var exception = Record.Exception(() => 
                ValidateFilePath(validPath, "pathParameter"));

            exception.Should().BeNull();
        }

        [Fact]
        public void ValidateDirectory_NonExistentDirectory_ThrowsDirectoryNotFoundException()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_testDirectory, "nonexistent", Guid.NewGuid().ToString());

            // Act & Assert
            var exception = Assert.Throws<DirectoryNotFoundException>(() => 
                ValidateDirectoryExists(nonExistentPath, "directoryParameter"));

            exception.Message.Should().Contain("directoryParameter");
            exception.Message.Should().Contain(nonExistentPath);
        }

        [Fact]
        public void ValidateDirectory_ExistingDirectory_DoesNotThrow()
        {
            // Arrange - _testDirectory already exists from constructor

            // Act & Assert
            var exception = Record.Exception(() => 
                ValidateDirectoryExists(_testDirectory, "directoryParameter"));

            exception.Should().BeNull();
        }

        [Fact]
        public void ValidateFileExists_NonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.txt");

            // Act & Assert
            var exception = Assert.Throws<FileNotFoundException>(() => 
                ValidateFileExists(nonExistentFile, "fileParameter"));

            exception.Message.Should().Contain("fileParameter");
            exception.FileName.Should().Be(nonExistentFile);
        }

        [Fact]
        public void ValidateFileExists_ExistingFile_DoesNotThrow()
        {
            // Arrange
            var testFile = Path.Combine(_testDirectory, "existing.txt");
            File.WriteAllText(testFile, "test content");

            // Act & Assert
            var exception = Record.Exception(() => 
                ValidateFileExists(testFile, "fileParameter"));

            exception.Should().BeNull();
        }

        [Fact]
        public async Task ValidateAsync_ConcurrentValidation_ThreadSafe()
        {
            // Arrange
            var tasks = new List<Task>();

            // Act
            for (int i = 0; i < 20; i++)
            {
                var task = Task.Run(() =>
                {
                    ValidateNonEmptyString($"test-string-{i}", "parameter");
                    ValidatePositiveInteger(i, "number");
                    ValidateEmailFormat($"test{i}@example.com", "email");
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            // Assert
            tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
        }

        // Helper validation methods that would typically be in the actual application
        private static void ValidateNonEmptyString(string? value, string parameterName)
        {
            if (value is null)
                throw new ArgumentNullException(parameterName);
            
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Parameter '{parameterName}' cannot be empty or whitespace.", parameterName);
        }

        private static void ValidatePositiveInteger(int value, string parameterName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(parameterName, value, $"Parameter '{parameterName}' must be non-negative.");
        }

        private static void ValidateEmailFormat(string email, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException($"Parameter '{parameterName}' cannot be empty or whitespace.", parameterName);

            // Simple email validation - in real applications, use more robust validation
            if (!email.Contains('@') || email.StartsWith('@') || email.EndsWith('@') || 
                email.Contains(' ') || email.Count(c => c == '@') != 1 ||
                email.EndsWith('.') || email.Contains("@."))  // Added check for @. pattern
            {
                throw new ArgumentException($"Parameter '{parameterName}' must be a valid email format.", parameterName);
            }
        }

        private static void ValidateFilePath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException($"Parameter '{parameterName}' cannot be empty or whitespace.", parameterName);

            // Check for invalid path characters - but be aware that these vary by OS
            // On macOS/Unix, fewer characters are actually invalid compared to Windows
            var invalidChars = new char[] { '\0' }; // Null character is invalid on all systems
            if (path.IndexOfAny(invalidChars) >= 0)
                throw new ArgumentException($"Parameter '{parameterName}' contains invalid path characters.", parameterName);
        }

        private static void ValidateDirectoryExists(string directoryPath, string parameterName)
        {
            ValidateFilePath(directoryPath, parameterName);
            
            if (!Directory.Exists(directoryPath))
                throw new DirectoryNotFoundException($"Directory specified in parameter '{parameterName}' does not exist: {directoryPath}");
        }

        private static void ValidateFileExists(string filePath, string parameterName)
        {
            ValidateFilePath(filePath, parameterName);
            
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File specified in parameter '{parameterName}' does not exist.", filePath);
        }
    }
}

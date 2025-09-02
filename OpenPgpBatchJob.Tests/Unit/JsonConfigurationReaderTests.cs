using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using OpenPgpBatchJob;
using Xunit;

namespace OpenPgpBatchJob.Tests.Unit
{
    public class JsonConfigurationReaderTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly List<string> _createdFiles;

        public JsonConfigurationReaderTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "OpenPgpBatchJobTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
            _createdFiles = [];
        }

        [Fact]
        public void LoadFromJsonFile_ValidJsonFile_ReturnsCorrectDictionary()
        {
            // Arrange
            var testData = new Dictionary<string, object>
            {
                ["SenderEmailAddress"] = "test@example.com",
                ["RecipientEmailAddress"] = "recipient@example.com",
                ["ModeOfOperation"] = "SENDER",
                ["SourceFolderPath"] = "/tmp/test/src"
            };

            var jsonContent = JsonSerializer.Serialize(testData, new JsonSerializerOptions { WriteIndented = true });
            var testFilePath = CreateTestFile("valid-config.json", jsonContent);

            // Act
            var result = JsonConfigurationReader.LoadFromJsonFile(testFilePath);

            // Assert
            result.Should().NotBeNull();
            result.Should().ContainKey("SenderEmailAddress").WhoseValue.Should().Be("test@example.com");
            result.Should().ContainKey("RecipientEmailAddress").WhoseValue.Should().Be("recipient@example.com");
            result.Should().ContainKey("ModeOfOperation").WhoseValue.Should().Be("SENDER");
            result.Should().ContainKey("SourceFolderPath").WhoseValue.Should().Be("/tmp/test/src");
        }

        [Fact]
        public void LoadFromJsonFile_NestedJsonStructure_FlattensDictionary()
        {
            // Arrange
            var testData = new
            {
                FolderConfiguration = new
                {
                    SourceFolderPath = "/tmp/src",
                    DestinationFolderPath = "/tmp/dest"
                },
                OperationMode = new
                {
                    ModeOfOperation = "RECIPIENT"
                },
                SenderConfiguration = new
                {
                    SenderEmailAddress = "sender@test.com"
                }
            };

            var jsonContent = JsonSerializer.Serialize(testData, new JsonSerializerOptions { WriteIndented = true });
            var testFilePath = CreateTestFile("nested-config.json", jsonContent);

            // Act
            var result = JsonConfigurationReader.LoadFromJsonFile(testFilePath);

            // Assert
            result.Should().NotBeNull();
            result.Should().ContainKey("SourceFolderPath").WhoseValue.Should().Be("/tmp/src");
            result.Should().ContainKey("DestinationFolderPath").WhoseValue.Should().Be("/tmp/dest");
            result.Should().ContainKey("ModeOfOperation").WhoseValue.Should().Be("RECIPIENT");
            result.Should().ContainKey("SenderEmailAddress").WhoseValue.Should().Be("sender@test.com");
        }

        [Fact]
        public void LoadFromJsonFile_EmptyJsonFile_ReturnsEmptyDictionary()
        {
            // Arrange
            var testFilePath = CreateTestFile("empty.json", "{}");

            // Act
            var result = JsonConfigurationReader.LoadFromJsonFile(testFilePath);

            // Assert
            result.Should().NotBeNull();
            result.Should().BeEmpty();
        }

        [Fact]
        public void LoadFromJsonFile_NonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_testDirectory, "non-existent.json");

            // Act & Assert
            var exception = Assert.Throws<FileNotFoundException>(() => 
                JsonConfigurationReader.LoadFromJsonFile(nonExistentPath));
            
            exception.Message.Should().Contain(nonExistentPath);
        }

        [Fact]
        public void LoadFromJsonFile_InvalidJsonContent_ThrowsInvalidDataException()
        {
            // Arrange
            var invalidJsonContent = "{ invalid json content }";
            var testFilePath = CreateTestFile("invalid.json", invalidJsonContent);

            // Act & Assert
            Assert.Throws<InvalidDataException>(() => 
                JsonConfigurationReader.LoadFromJsonFile(testFilePath));
        }

        [Fact]
        public void LoadFromJsonFile_NullOrEmptyPath_ThrowsFileNotFoundException()
        {
            // Act & Assert
            Assert.Throws<FileNotFoundException>(() => 
                JsonConfigurationReader.LoadFromJsonFile(null));
            
            Assert.Throws<FileNotFoundException>(() => 
                JsonConfigurationReader.LoadFromJsonFile(""));
            
            Assert.Throws<FileNotFoundException>(() => 
                JsonConfigurationReader.LoadFromJsonFile("   "));
        }

        [Fact]
        public void LoadFromJsonFile_ArrayValues_ConvertsToString()
        {
            // Arrange
            var testData = new
            {
                StringValue = "test",
                ArrayValue = new[] { "item1", "item2", "item3" },
                NumberValue = 42,
                BooleanValue = true
            };

            var jsonContent = JsonSerializer.Serialize(testData, new JsonSerializerOptions { WriteIndented = true });
            var testFilePath = CreateTestFile("mixed-types.json", jsonContent);

            // Act
            var result = JsonConfigurationReader.LoadFromJsonFile(testFilePath);

            // Assert
            result.Should().NotBeNull();
            result.Should().ContainKey("StringValue").WhoseValue.Should().Be("test");
            result.Should().ContainKey("NumberValue").WhoseValue.Should().Be("42");
            result.Should().ContainKey("BooleanValue").WhoseValue.Should().Be("True");
            
            // Arrays are indexed with numeric keys in .NET Configuration
            result.Should().ContainKey("0").WhoseValue.Should().Be("item1");
            result.Should().ContainKey("1").WhoseValue.Should().Be("item2");
            result.Should().ContainKey("2").WhoseValue.Should().Be("item3");
        }

        [Fact]
        public void LoadFromJsonFile_RealConfigurationFile_LoadsAllExpectedKeys()
        {
            // Arrange - Create a config similar to the actual application config
            var realConfigContent = @"{
  ""FolderConfiguration"": {
    ""SourceFolderPath"": ""/tmp/Sender/src"",
    ""DestinationFolderPath"": ""/tmp/Sender/dest"",
    ""ArchiveFolderPath"": ""/tmp/Sender/archive"",
    ""DestinationFilePrefix"": ""ENC_"",
    ""ArchiveFilePrefix"": """"
  },
  ""OperationMode"": {
    ""ModeOfOperation"": ""SENDER""
  },
  ""PassphraseProtection"": {
    ""PassphraseProtectionMode"": ""ASPNET_DPAPI"",
    ""entropy"": ""CD639595-9BA7-4F10-9FE7-07852D14ADE4: Salt Is Not A Password"",
    ""SSLCertDistinguishedSubjectName"": ""CN=Application X ASP.NET Core Data Protection Certificate""
  },
  ""SenderConfiguration"": {
    ""SenderEmailAddress"": ""alice@home.internal"",
    ""SenderAWSSecretsName"": ""prod/AliceSecretPassPhrase""
  },
  ""RecipientConfiguration"": {
    ""RecipientEmailAddress"": ""bob@home.internal"",
    ""RecipientAWSSecretsName"": ""prod/BobSecretPassPhrase""
  }
}";

            var testFilePath = CreateTestFile("real-config.json", realConfigContent);

            // Act
            var result = JsonConfigurationReader.LoadFromJsonFile(testFilePath);

            // Assert
            result.Should().NotBeNull();
            
            // Verify all expected keys are present
            result.Should().ContainKey("SourceFolderPath").WhoseValue.Should().Be("/tmp/Sender/src");
            result.Should().ContainKey("DestinationFolderPath").WhoseValue.Should().Be("/tmp/Sender/dest");
            result.Should().ContainKey("ArchiveFolderPath").WhoseValue.Should().Be("/tmp/Sender/archive");
            result.Should().ContainKey("DestinationFilePrefix").WhoseValue.Should().Be("ENC_");
            result.Should().ContainKey("ModeOfOperation").WhoseValue.Should().Be("SENDER");
            result.Should().ContainKey("PassphraseProtectionMode").WhoseValue.Should().Be("ASPNET_DPAPI");
            result.Should().ContainKey("SenderEmailAddress").WhoseValue.Should().Be("alice@home.internal");
            result.Should().ContainKey("RecipientEmailAddress").WhoseValue.Should().Be("bob@home.internal");
            result.Should().ContainKey("entropy");
            result.Should().ContainKey("SSLCertDistinguishedSubjectName");
            result.Should().ContainKey("SenderAWSSecretsName");
            result.Should().ContainKey("RecipientAWSSecretsName");
        }

        private string CreateTestFile(string fileName, string content)
        {
            var filePath = Path.Combine(_testDirectory, fileName);
            File.WriteAllText(filePath, content);
            _createdFiles.Add(filePath);
            return filePath;
        }

        public void Dispose()
        {
            // Clean up test files
            foreach (var file in _createdFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }

            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
    }
}

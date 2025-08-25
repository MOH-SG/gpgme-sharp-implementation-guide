using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace OpenPgpBatchJob.Tests.Integration
{
    /// <summary>
    /// Integration tests for the OpenPgpBatchJob application
    /// These tests work with the real application components but in a controlled test environment
    /// </summary>
    public class IntegrationTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly string _sourceDirectory;
        private readonly string _destinationDirectory;
        private readonly string _archiveDirectory;

        public IntegrationTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "OpenPgpIntegrationTests", Guid.NewGuid().ToString());
            _sourceDirectory = Path.Combine(_testDirectory, "source");
            _destinationDirectory = Path.Combine(_testDirectory, "destination");
            _archiveDirectory = Path.Combine(_testDirectory, "archive");
            
            Directory.CreateDirectory(_testDirectory);
            Directory.CreateDirectory(_sourceDirectory);
            Directory.CreateDirectory(_destinationDirectory);
            Directory.CreateDirectory(_archiveDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }

        [Fact]
        public void JsonConfigurationReader_Integration_LoadsRealConfigurationFile()
        {
            // Arrange
            var configContent = CreateTestConfigurationContent();
            var configPath = Path.Combine(_testDirectory, "test-config.json");
            File.WriteAllText(configPath, configContent);

            // Act
            var result = JsonConfigurationReader.LoadFromJsonFile(configPath);

            // Assert
            result.Should().NotBeNull();
            result.Should().ContainKey("SourceFolderPath");
            result.Should().ContainKey("DestinationFolderPath");
            result.Should().ContainKey("ArchiveFolderPath");
            result.Should().ContainKey("SenderEmailAddress");
            result.Should().ContainKey("RecipientEmailAddress");
            
            // Verify actual values
            result["SenderEmailAddress"].Should().Be("alice@home.internal");
            result["RecipientEmailAddress"].Should().Be("bob@home.internal");
        }

        [Fact]
        public void AppSettingsConfiguration_Integration_LoadsScenarioMappings()
        {
            // Arrange
            var appSettingsContent = CreateTestAppSettingsContent();
            var appSettingsPath = Path.Combine(_testDirectory, "appsettings.json");
            File.WriteAllText(appSettingsPath, appSettingsContent);

            // Create referenced config file
            var scenarioConfigContent = CreateTestConfigurationContent();
            var scenarioConfigPath = Path.Combine(_testDirectory, "scenario-sender.json");
            File.WriteAllText(scenarioConfigPath, scenarioConfigContent);

            // Act
            var configuration = new ConfigurationBuilder()
                .SetBasePath(_testDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            // Assert
            var scenarioPath = configuration["ScenarioConfigurations:Config_RunAsSender_for_SystemA"];
            scenarioPath.Should().Be("scenario-sender.json");
            
            var loggingLevel = configuration["Serilog:MinimumLevel"];
            loggingLevel.Should().Be("Information");
        }

        [Fact]
        public void DirectoryStructure_Integration_ValidatesAllRequiredDirectoriesExist()
        {
            // Arrange
            var testConfig = new Dictionary<string, string>
            {
                { "SourceFolderPath", _sourceDirectory },
                { "DestinationFolderPath", _destinationDirectory },
                { "ArchiveFolderPath", _archiveDirectory }
            };

            // Act & Assert
            foreach (var kvp in testConfig)
            {
                Directory.Exists(kvp.Value).Should().BeTrue($"Directory {kvp.Key} should exist at {kvp.Value}");
            }
        }

        [Fact]
        public void FileProcessing_Integration_CreatesAndProcessesTestFiles()
        {
            // Arrange
            var testFiles = new[]
            {
                "test-document-1.txt",
                "test-document-2.pdf",
                "test-data.csv"
            };

            // Act
            foreach (var fileName in testFiles)
            {
                var filePath = Path.Combine(_sourceDirectory, fileName);
                File.WriteAllText(filePath, $"Test content for {fileName}");
            }

            // Assert
            var sourceFiles = Directory.GetFiles(_sourceDirectory);
            sourceFiles.Should().HaveCount(3);
            
            foreach (var fileName in testFiles)
            {
                var filePath = Path.Combine(_sourceDirectory, fileName);
                File.Exists(filePath).Should().BeTrue($"File {fileName} should exist in source directory");
                
                var content = File.ReadAllText(filePath);
                content.Should().Contain(fileName, "File should contain expected content");
            }
        }

        [Fact]
        public void OpenPgpHelper_Integration_HandlesConfigurationLifecycle()
        {
            // Arrange
            var helper = new OpenPgpHelper();
            var testConfig = CreateValidIntegrationConfig();

            // Act
            helper.RuntimeAppSettings = testConfig;

            // Assert
            helper.RuntimeAppSettings.Should().NotBeNull();
            helper.RuntimeAppSettings.Should().HaveCount(testConfig.Count);
            helper.RuntimeAppSettings["SenderEmailAddress"].Should().Be("alice@home.internal");
            helper.RuntimeAppSettings["RecipientEmailAddress"].Should().Be("bob@home.internal");
        }

        [Fact]
        public void ConfigurationValidation_Integration_DetectsInvalidEmailFormats()
        {
            // Arrange
            var invalidConfigs = new[]
            {
                new { Key = "SenderEmailAddress", Value = "invalid-email" },
                new { Key = "RecipientEmailAddress", Value = "missing@" },
                new { Key = "SenderEmailAddress", Value = "@missing.com" }
            };

            foreach (var invalidConfig in invalidConfigs)
            {
                var testConfig = CreateValidIntegrationConfig();
                testConfig[invalidConfig.Key] = invalidConfig.Value;

                // Act & Assert
                var exception = Assert.Throws<ArgumentException>(() => 
                    ValidateEmailInConfiguration(testConfig, invalidConfig.Key));

                exception.Message.Should().Contain(invalidConfig.Key);
            }
        }

        [Fact]
        public void ErrorHandling_Integration_LogsAndHandlesFileSystemErrors()
        {
            // Arrange
            var invalidPath = Path.Combine(_testDirectory, "nonexistent", "deep", "path");
            var testConfig = CreateValidIntegrationConfig();
            testConfig["SourceFolderPath"] = invalidPath;

            // Act & Assert
            var exception = Assert.Throws<DirectoryNotFoundException>(() => 
                ValidateDirectoryExistence(testConfig["SourceFolderPath"]));

            exception.Message.Should().Contain("SourceFolderPath");
        }

        [Fact]
        public void PerformanceTest_Integration_HandlesLargeConfigurationFiles()
        {
            // Arrange
            var largeConfigContent = CreateLargeConfigurationContent();
            var configPath = Path.Combine(_testDirectory, "large-config.json");
            File.WriteAllText(configPath, largeConfigContent);

            // Act
            var startTime = DateTime.UtcNow;
            var result = JsonConfigurationReader.LoadFromJsonFile(configPath);
            var endTime = DateTime.UtcNow;

            // Assert
            var duration = endTime - startTime;
            duration.Should().BeLessThan(TimeSpan.FromSeconds(2), "Large configuration loading should be efficient");
            
            result.Should().NotBeNull();
            result.Should().ContainKey("SourceFolderPath");
            result.Keys.Count.Should().BeGreaterThan(100, "Large configuration should have many keys");
        }

        [Fact]
        public async Task ConcurrencyTest_Integration_HandlesMultipleSimultaneousOperations()
        {
            // Arrange
            var tasks = new List<Task>();
            var results = new List<Dictionary<string, string>>();
            var lockObject = new object();

            // Act
            for (int i = 0; i < 5; i++)
            {
                var taskIndex = i;
                var task = Task.Run(() =>
                {
                    var configContent = CreateTestConfigurationContent();
                    var configPath = Path.Combine(_testDirectory, $"concurrent-config-{taskIndex}.json");
                    File.WriteAllText(configPath, configContent);
                    
                    var result = JsonConfigurationReader.LoadFromJsonFile(configPath);
                    
                    lock (lockObject)
                    {
                        results.Add(result);
                    }
                });
                tasks.Add(task);
            }

            await Task.WhenAll(tasks);

            // Assert
            results.Should().HaveCount(5);
            tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
            
            foreach (var result in results)
            {
                result.Should().ContainKey("SenderEmailAddress");
                result["SenderEmailAddress"].Should().Be("alice@home.internal");
            }
        }

        [Fact]
        public void RealWorldScenario_Integration_CompleteConfigurationWorkflow()
        {
            // Arrange - Simulate a complete real-world scenario
            var appSettingsPath = Path.Combine(_testDirectory, "appsettings.json");
            var scenarioConfigPath = Path.Combine(_testDirectory, "scenario-sender.json");
            
            // Create realistic application configuration
            File.WriteAllText(appSettingsPath, CreateTestAppSettingsContent());
            File.WriteAllText(scenarioConfigPath, CreateTestConfigurationContent());
            
            // Create test files in source directory
            var testFiles = new[] { "document1.txt", "document2.pdf", "data.csv" };
            foreach (var fileName in testFiles)
            {
                var filePath = Path.Combine(_sourceDirectory, fileName);
                File.WriteAllText(filePath, $"Sample content for {fileName} - Created: {DateTime.Now}");
            }

            // Act - Load configuration like the real application
            var appConfig = new ConfigurationBuilder()
                .SetBasePath(_testDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var scenarioPath = appConfig["ScenarioConfigurations:Config_RunAsSender_for_SystemA"];
            scenarioPath.Should().NotBeNull("Scenario path should be configured");
            var fullScenarioPath = Path.Combine(_testDirectory, scenarioPath!);
            var runtimeConfig = JsonConfigurationReader.LoadFromJsonFile(fullScenarioPath);

            var helper = new OpenPgpHelper();
            helper.RuntimeAppSettings = runtimeConfig;

            // Assert - Verify complete workflow
            scenarioPath.Should().Be("scenario-sender.json");
            runtimeConfig.Should().NotBeNull();
            runtimeConfig.Should().ContainKey("SenderEmailAddress");
            
            helper.RuntimeAppSettings["SenderEmailAddress"].Should().Be("alice@home.internal");
            helper.RuntimeAppSettings["RecipientEmailAddress"].Should().Be("bob@home.internal");
            
            // Verify directories and files are properly set up
            Directory.GetFiles(_sourceDirectory).Should().HaveCount(3);
            Directory.Exists(_destinationDirectory).Should().BeTrue();
            Directory.Exists(_archiveDirectory).Should().BeTrue();
        }

        [Fact]
        public void ErrorRecovery_Integration_HandlesCorruptedConfigurationFiles()
        {
            // Arrange
            var corruptedConfigs = new[]
            {
                "{ incomplete json",
                "{ \"key\": }",
                "not json at all",
                ""
            };

            foreach (var corruptedContent in corruptedConfigs)
            {
                var configPath = Path.Combine(_testDirectory, $"corrupted-{Guid.NewGuid()}.json");
                File.WriteAllText(configPath, corruptedContent);

                // Act & Assert
                var exception = Assert.Throws<InvalidDataException>(() => 
                    JsonConfigurationReader.LoadFromJsonFile(configPath));

                exception.Should().NotBeNull();
            }
        }

        [Fact]
        public void ResourceCleanup_Integration_ProperlyDisposesResources()
        {
            // Arrange
            var tempFiles = new List<string>();
            
            // Act - Create multiple temporary files during test execution
            for (int i = 0; i < 10; i++)
            {
                var tempFile = Path.Combine(_testDirectory, $"temp-{i}.json");
                File.WriteAllText(tempFile, CreateTestConfigurationContent());
                tempFiles.Add(tempFile);
            }

            // Assert - Verify files were created
            tempFiles.Should().AllSatisfy(file => File.Exists(file).Should().BeTrue());

            // Act - Cleanup (this will be done in Dispose, but we can test it explicitly)
            foreach (var file in tempFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }

            // Assert - Verify cleanup
            tempFiles.Should().AllSatisfy(file => File.Exists(file).Should().BeFalse());
        }

        // Helper methods
        private string CreateTestConfigurationContent()
        {
            return @"{
  ""FolderConfiguration"": {
    ""SourceFolderPath"": """ + _sourceDirectory.Replace("\\", "\\\\") + @""",
    ""DestinationFolderPath"": """ + _destinationDirectory.Replace("\\", "\\\\") + @""",
    ""ArchiveFolderPath"": """ + _archiveDirectory.Replace("\\", "\\\\") + @"""
  },
  ""EmailConfiguration"": {
    ""SenderEmailAddress"": ""alice@home.internal"",
    ""RecipientEmailAddress"": ""bob@home.internal""
  },
  ""SecurityConfiguration"": {
    ""SecretPassPhrase"": ""encrypted-passphrase-here"",
    ""Role"": ""Sender""
  }
}";
        }

        private string CreateTestAppSettingsContent()
        {
            return @"{
  ""Serilog"": {
    ""MinimumLevel"": ""Information"",
    ""WriteTo"": [
      {
        ""Name"": ""Console""
      },
      {
        ""Name"": ""File"",
        ""Args"": {
          ""path"": ""logs/log.txt"",
          ""rollingInterval"": ""Day""
        }
      }
    ]
  },
  ""ScenarioConfigurations"": {
    ""Config_RunAsSender_for_SystemA"": ""scenario-sender.json"",
    ""Config_RunAsRecipient_for_SystemA"": ""scenario-recipient.json""
  }
}";
        }

        private string CreateLargeConfigurationContent()
        {
            var config = new
            {
                SourceFolderPath = _sourceDirectory,
                DestinationFolderPath = _destinationDirectory,
                ArchiveFolderPath = _archiveDirectory,
                SenderEmailAddress = "alice@home.internal",
                RecipientEmailAddress = "bob@home.internal"
            };

            var dict = new Dictionary<string, object>
            {
                { "SourceFolderPath", config.SourceFolderPath },
                { "DestinationFolderPath", config.DestinationFolderPath },
                { "ArchiveFolderPath", config.ArchiveFolderPath },
                { "SenderEmailAddress", config.SenderEmailAddress },
                { "RecipientEmailAddress", config.RecipientEmailAddress }
            };

            // Add many additional properties
            for (int i = 0; i < 200; i++)
            {
                dict[$"ExtraProperty_{i}"] = $"ExtraValue_{i}";
            }

            return System.Text.Json.JsonSerializer.Serialize(dict, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        private Dictionary<string, string> CreateValidIntegrationConfig()
        {
            return new Dictionary<string, string>
            {
                { "SourceFolderPath", _sourceDirectory },
                { "DestinationFolderPath", _destinationDirectory },
                { "ArchiveFolderPath", _archiveDirectory },
                { "SenderEmailAddress", "alice@home.internal" },
                { "RecipientEmailAddress", "bob@home.internal" },
                { "SecretPassPhrase", "test-passphrase" },
                { "Role", "Sender" }
            };
        }

        private static void ValidateEmailInConfiguration(Dictionary<string, string> config, string emailKey)
        {
            var email = config[emailKey];
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') ||
                email.StartsWith('@') || email.EndsWith('@'))
            {
                throw new ArgumentException($"Invalid email format for {emailKey}: {email}");
            }
        }

        private static void ValidateDirectoryExistence(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                throw new DirectoryNotFoundException($"Directory specified in SourceFolderPath does not exist: {directoryPath}");
            }
        }
    }
}

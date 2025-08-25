using System;
using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

#pragma warning disable CS8604 // Possible null reference argument - Test files are controlled

namespace OpenPgpBatchJob.Tests.Unit
{
    public class ConfigurationTests : IDisposable
    {
        private readonly string _testDirectory;
        private readonly List<string> _createdFiles;

        public ConfigurationTests()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), "OpenPgpConfigTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDirectory);
            _createdFiles = new List<string>();
        }

        [Theory]
        [InlineData("Config_RunAsSender_for_SystemA", "asSenderForSystemA.json")]
        [InlineData("Config_RunAsRecipient_for_SystemA", "asRecipientForSystemA.json")]
        public void Configuration_ValidScenario_LoadsCorrectMapping(string scenarioKey, string expectedFileName)
        {
            // Arrange
            var appSettingsContent = $@"{{
  ""ScenarioConfigurations"": {{
    ""{scenarioKey}"": ""{expectedFileName}""
  }}
}}";

            var appSettingsPath = CreateTestFile("appsettings.json", appSettingsContent);
            var configRoot = Path.GetDirectoryName(appSettingsPath) ?? throw new InvalidOperationException("Failed to get directory name");

            var configuration = new ConfigurationBuilder()
                .SetBasePath(configRoot)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Act
            var scenarioConfigPath = configuration[$"ScenarioConfigurations:{scenarioKey}"];

            // Assert
            scenarioConfigPath.Should().NotBeNull();
            scenarioConfigPath.Should().Be(expectedFileName);
        }

        [Fact]
        public void Configuration_MissingScenario_ReturnsNull()
        {
            // Arrange
            var appSettingsContent = @"{
  ""ScenarioConfigurations"": {
    ""Config_RunAsSender_for_SystemA"": ""asSenderForSystemA.json""
  }
}";

            var appSettingsPath = CreateTestFile("appsettings.json", appSettingsContent);
            var configRoot = Path.GetDirectoryName(appSettingsPath) ?? throw new InvalidOperationException("Failed to get directory name");

            var configuration = new ConfigurationBuilder()
                .SetBasePath(configRoot)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Act
            var scenarioConfigPath = configuration["ScenarioConfigurations:NonExistentScenario"];

            // Assert
            scenarioConfigPath.Should().BeNull();
        }

        [Fact]
        public void Configuration_EmptyScenarioConfigurations_ReturnsNull()
        {
            // Arrange
            var appSettingsContent = @"{
  ""ScenarioConfigurations"": {}
}";

            var appSettingsPath = CreateTestFile("appsettings.json", appSettingsContent);
            var configRoot = Path.GetDirectoryName(appSettingsPath);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(configRoot)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Act
            var scenarioConfigPath = configuration["ScenarioConfigurations:AnyScenario"];

            // Assert
            scenarioConfigPath.Should().BeNull();
        }

        [Fact]
        public void Configuration_ValidAppSettings_LoadsSerilogConfiguration()
        {
            // Arrange
            var appSettingsContent = @"{
  ""Serilog"": {
    ""Using"": [""Serilog.Sinks.Console"", ""Serilog.Sinks.File""],
    ""MinimumLevel"": ""Information"",
    ""WriteTo"": [
      { ""Name"": ""Console"" },
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
    ""Config_RunAsSender_for_SystemA"": ""asSenderForSystemA.json""
  }
}";

            var appSettingsPath = CreateTestFile("appsettings.json", appSettingsContent);
            var configRoot = Path.GetDirectoryName(appSettingsPath);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(configRoot)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Act & Assert
            configuration["Serilog:MinimumLevel"].Should().Be("Information");
            configuration["Serilog:WriteTo:0:Name"].Should().Be("Console");
            configuration["Serilog:WriteTo:1:Name"].Should().Be("File");
            configuration["Serilog:WriteTo:1:Args:path"].Should().Be("logs/log.txt");
            configuration["Serilog:WriteTo:1:Args:rollingInterval"].Should().Be("Day");
        }

        [Fact]
        public void Configuration_MissingAppSettingsFile_ThrowsException()
        {
            // Arrange
            var nonExistentPath = Path.Combine(_testDirectory, "missing");

            // Act & Assert  
            // Note: SetBasePath throws DirectoryNotFoundException if directory doesn't exist
            var exception = Assert.Throws<DirectoryNotFoundException>(() =>
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(nonExistentPath)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();
            });

            exception.Message.Should().Contain(nonExistentPath);
        }

        [Fact]
        public void Configuration_InvalidJsonInAppSettings_ThrowsException()
        {
            // Arrange
            var invalidAppSettingsContent = "{ invalid json }";
            var appSettingsPath = CreateTestFile("appsettings.json", invalidAppSettingsContent);
            var configRoot = Path.GetDirectoryName(appSettingsPath);

            // Act & Assert
            Assert.Throws<InvalidDataException>(() =>
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(configRoot)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();
            });
        }

        [Theory]
        [InlineData("Config_RunAsSender_for_SystemA")]
        [InlineData("Config_RunAsRecipient_for_SystemA")]
        [InlineData("Custom_Config_Test")]
        public void Configuration_CaseInsensitiveKeys_ReturnsCorrectValue(string scenarioKey)
        {
            // Arrange
            var appSettingsContent = $@"{{
  ""ScenarioConfigurations"": {{
    ""{scenarioKey}"": ""test-config.json""
  }}
}}";

            var appSettingsPath = CreateTestFile("appsettings.json", appSettingsContent);
            var configRoot = Path.GetDirectoryName(appSettingsPath);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(configRoot)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Act
            var scenarioConfigPath = configuration[$"ScenarioConfigurations:{scenarioKey}"];
            var scenarioConfigPathLower = configuration[$"scenarioconfigurations:{scenarioKey.ToLower()}"];

            // Assert
            scenarioConfigPath.Should().Be("test-config.json");
            // Note: .NET Configuration is case-insensitive for both section names and keys
            scenarioConfigPathLower.Should().Be("test-config.json"); // Configuration is case-insensitive
        }

        [Fact]
        public void Configuration_RealWorldAppSettings_LoadsAllSections()
        {
            // Arrange - Mimic the actual appsettings.json structure
            var realAppSettingsContent = @"{
  ""Serilog"": {
    ""Using"": [ ""Serilog.Sinks.Console"", ""Serilog.Sinks.File"" ],
    ""MinimumLevel"": ""Information"",
    ""WriteTo"": [
      { ""Name"": ""Console"" },
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
    ""Config_RunAsSender_for_SystemA"": ""asSenderForSystemA.json"",
    ""Config_RunAsRecipient_for_SystemA"": ""asRecipientForSystemA.json""
  }
}";

            var appSettingsPath = CreateTestFile("appsettings.json", realAppSettingsContent);
            var configRoot = Path.GetDirectoryName(appSettingsPath);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(configRoot)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Act & Assert
            // Test Serilog configuration
            configuration["Serilog:MinimumLevel"].Should().Be("Information");
            configuration["Serilog:Using:0"].Should().Be("Serilog.Sinks.Console");
            configuration["Serilog:Using:1"].Should().Be("Serilog.Sinks.File");

            // Test ScenarioConfigurations
            configuration["ScenarioConfigurations:Config_RunAsSender_for_SystemA"].Should().Be("asSenderForSystemA.json");
            configuration["ScenarioConfigurations:Config_RunAsRecipient_for_SystemA"].Should().Be("asRecipientForSystemA.json");
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

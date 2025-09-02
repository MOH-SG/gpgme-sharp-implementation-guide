using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace OpenPgpBatchJob
{
    /// <summary>
    /// Helper Class to load JSON configurations and flatten them to key-value pairs
    /// for compatibility with the existing code structure
    /// </summary>
    public static class JsonConfigurationReader
    {
        public static Dictionary<string, string> LoadFromJsonFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Configuration file [{filePath}] NOT FOUND!");
            }

            var configurationBuilder = new ConfigurationBuilder()
                .AddJsonFile(filePath, optional: false, reloadOnChange: false);

            var configuration = configurationBuilder.Build();
            
            return FlattenConfiguration(configuration);
        }

        private static Dictionary<string, string> FlattenConfiguration(IConfiguration configuration)
        {
            var result = new Dictionary<string, string>();

            foreach (var section in configuration.GetChildren())
            {
                FlattenSection(section, "", result);
            }

            return result;
        }

        private static void FlattenSection(IConfigurationSection section, string prefix, Dictionary<string, string> result)
        {
            string currentKey = string.IsNullOrEmpty(prefix) ? section.Key : $"{prefix}:{section.Key}";

            if (section.Value != null)
            {
                // This is a leaf node - add the key-value pair
                result[section.Key] = section.Value;
            }
            else
            {
                // This is a parent node - recurse into children
                foreach (var child in section.GetChildren())
                {
                    FlattenSection(child, currentKey, result);
                }
            }
        }
    }
}

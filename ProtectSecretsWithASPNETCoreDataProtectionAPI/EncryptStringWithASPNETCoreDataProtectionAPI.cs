using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace ProtectSecretsWithASPNETCoreDataProtectionAPI
{
    /// <summary>
    /// This helper class uses ASP.NET Core Data Protection API to encrypt Secrets. 
    /// Credits: https://simplecodesoftware.com/articles/how-to-encrypt-data-on-macos-without-dpapi 
    /// </summary>
    public class SecretsEncryptor
    {
        /// <summary>
        /// Encrypt String using ASP.NET Core Data Protection API
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static string EncryptString(SecureString input)
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string? sEntropy = configuration["entropy"];
            string? SSLCertDistinguishedSubjectName = configuration["SSLCertDistinguishedSubjectName"];
            
            if (string.IsNullOrEmpty(sEntropy) || string.IsNullOrEmpty(SSLCertDistinguishedSubjectName))
            {
                throw new InvalidOperationException("Required configuration values 'entropy' and 'SSLCertDistinguishedSubjectName' must be provided in appsettings.json");
            }
            var serviceCollection = new ServiceCollection();
            SetupEnvironment.ConfigureServices(serviceCollection, SSLCertDistinguishedSubjectName);
            IDataProtector dataProtector = serviceCollection.BuildServiceProvider().GetDataProtector(purpose: sEntropy);

            byte[] rawSecretBytesToProtect = Encoding.Unicode.GetBytes(Util.ToInsecureString(input));
            byte[] protectedSecretBytes = dataProtector.Protect(rawSecretBytesToProtect);

            return Convert.ToBase64String(protectedSecretBytes, 0, protectedSecretBytes.Length);
        }

        
    }
}

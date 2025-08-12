using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace ProtectSecretsWithASPNETCoreDataProtectionAPI
{
    /// <summary>
    /// This helper class uses ASP.NET Core Data Protection API to Decrypt Secrets. 
    /// This helper class supports Windows, Linus and macOS Systems! 
    /// Credits: https://simplecodesoftware.com/articles/how-to-encrypt-data-on-macos-without-dpapi 
    /// </summary>
    public class SecretsDecryptor
    {
        /// <summary>
        /// Decrypt String using ASP.NET Core Data Protection API
        /// </summary>
        /// <param name="secret"></param>
        /// <returns></returns>
        public static SecureString DecryptString(string secret)
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            string? sEntropy = configuration["entropy"];
            string? sslCertDistinguishedSubjectName = configuration["SSLCertDistinguishedSubjectName"];
            
            if (string.IsNullOrEmpty(sEntropy) || string.IsNullOrEmpty(sslCertDistinguishedSubjectName))
            {
                throw new InvalidOperationException("Required configuration values 'entropy' and 'SSLCertDistinguishedSubjectName' must be provided in appsettings.json");
            }

            return DecryptString(secret, sEntropy, sslCertDistinguishedSubjectName);
        }
        /// <summary>
        /// Decrypt String using ASP.NET Core Data Protection API
        /// </summary>
        /// <param name="secret"></param>
        /// <returns></returns>
        public static SecureString DecryptString(string secret, string entropy, string sslCertDistinguishedSubjectName)
        {
            
            var serviceCollection2 = new ServiceCollection();
            SetupEnvironment.ConfigureServices(serviceCollection2, sslCertDistinguishedSubjectName);
            IDataProtector dataProtector2 = serviceCollection2.BuildServiceProvider().GetDataProtector(purpose: entropy);

            byte[] unprotectedSecretBytes = dataProtector2.Unprotect(Convert.FromBase64String(secret));
            return Util.ToSecureString(System.Text.Encoding.Unicode.GetString(unprotectedSecretBytes, 0, unprotectedSecretBytes.Length));
        }
    }
}

﻿#nullable enable
using Libgpgme;
using PgpCombinedCrypto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using System.IO;
using System.Configuration;
using Serilog;

namespace OpenPgpBatchJob
{
    public class OpenPgpHelper
    {
        public Dictionary<string, string> RuntimeAppSettings { get; set; } = [];
        public string SenderEmail 
        { 
            get => _senderEmail ?? throw new InvalidOperationException("SenderEmail not initialized"); 
            set => _senderEmail = value; 
        }
        public string RecipientEmail 
        { 
            get => _recipientEmail ?? throw new InvalidOperationException("RecipientEmail not initialized"); 
            set => _recipientEmail = value; 
        }
        public PgpKey RecipientKey 
        { 
            get => _recipientKey ?? throw new InvalidOperationException("RecipientKey not initialized"); 
            set => _recipientKey = value; 
        }
        public PgpKey SenderKey 
        { 
            get => _senderKey ?? throw new InvalidOperationException("SenderKey not initialized"); 
            set => _senderKey = value; 
        }

        private Context? _ctx;
        private string? _senderEmail;
        private string? _recipientEmail;
        private KeyStore? _keyring;
        private PgpKey? _recipientKey;
        private PgpKey? _senderKey;

        public OpenPgpHelper() { }

        public void Init()
        {
            _ctx = new Context();

            if (_ctx.Protocol != Protocol.OpenPGP)
                _ctx.SetEngineInfo(Protocol.OpenPGP, null, null);

            // Validate runtime settings are available
            if (RuntimeAppSettings == null)
                throw new InvalidOperationException("RuntimeAppSettings not set");

            // Validate required configuration keys exist
            if (!RuntimeAppSettings.TryGetValue("SenderEmailAddress", out var senderEmailValue) || string.IsNullOrWhiteSpace(senderEmailValue))
                throw new InvalidOperationException("SenderEmailAddress not configured");
                
            if (!RuntimeAppSettings.TryGetValue("RecipientEmailAddress", out var recipientEmailValue) || string.IsNullOrWhiteSpace(recipientEmailValue))
                throw new InvalidOperationException("RecipientEmailAddress not configured");

            SenderEmail = senderEmailValue.Trim();
            RecipientEmail = recipientEmailValue.Trim();

            String[] searchpattern = new String[2];
            searchpattern[0] = SenderEmail;
            searchpattern[1] = RecipientEmail;

            _keyring = _ctx.KeyStore;

            // We want the key signatures!
            _ctx.KeylistMode = KeylistMode.Signatures;

            // retrieve all keys that have Sender's or Recipient's email address
            Key[] keys = _keyring.GetKeyList(searchpattern, false);


            if (keys != null && keys.Length != 0)
            {
                foreach (Key k in keys)
                {
                    if (k.Uid != null)
                    {
                        if (RecipientKey == null && k.Uid.Email.Equals(RecipientEmail))
                            RecipientKey = (PgpKey)k;
                        if (SenderKey == null && k.Uid.Email.Equals(SenderEmail))
                            SenderKey = (PgpKey)k;
                    }
                    else
                        throw new InvalidKeyException();
                }
            }

            Log.Information(PrintEngineInfoProperties(_ctx.EngineInfo));

        }

        /// <summary>
        /// Encrypt and Sign File using OpenPGP 
        /// </summary>
        /// <param name="sourceFilePath">Path of the Source File that is being Encrypted and Signed</param>
        /// <param name="destinationFilePath">Path of the Destination File</param>
        /// <param name="archiveFilePath">Path for archiving the Source File after a successful operation. If null, then source file will remain in the source folder after processing.</param>
        public void EncryptAndSignFile(string sourceFilePath, string destinationFilePath, string? archiveFilePath)
        {
            // Validate initialization
            if (_ctx == null)
                throw new InvalidOperationException("OpenPgpHelper not initialized. Call Init() first.");
            if (_recipientKey == null)
                throw new InvalidOperationException("RecipientKey not set. Call Init() first.");
            if (_senderKey == null)
                throw new InvalidOperationException("SenderKey not set. Call Init() first.");

            /////// ENCRYPT AND SIGN DATA ///////
            GpgmeData plain = new GpgmeFileData(sourceFilePath);
            GpgmeData cipher = new GpgmeFileData(destinationFilePath);

            // Create ASCII armored output. The default is to create the binary OpenPGP format.
            _ctx.Armor = true;

            /* Set the password callback 
             */
            _ctx.ClearPassphraseFunction();
            _ctx.SetPassphraseFunction(SenderPassphraseCallback);
            _ctx.PinentryMode = PinentryMode.Loopback; // Use the Loopback option to supply the secretPassphrase programmatically
            _ctx.Signers.Clear();
            _ctx.Signers.Add(_senderKey);

            Log.Information($"Source File Size: {plain.Length / 1024.0:N2} KBytes...");
            EncryptionResult encrst = _ctx.EncryptAndSign(
                new Key[] { _recipientKey },
                EncryptFlags.AlwaysTrust,
                plain,
                cipher);

            Log.Information($"Destination File Size: {cipher.Length / 1024.0:N2} KBytes...");

            plain.Close();
            cipher.Close();

            // print out invalid signature keys
            if (encrst.InvalidRecipients != null)
            {
                foreach (InvalidKey key in encrst.InvalidRecipients)
                    Log.Error(string.Format("Invalid key: {0} ({1})",
                        key.Fingerprint,
                        key.Reason));
            }
            else
            {
                Log.Information(string.Format("Successfully Encrypted and Signed [{0}] and saved it to [{1}]!", sourceFilePath, destinationFilePath));

                if (!string.IsNullOrEmpty(archiveFilePath))
                {
                    try
                    {
                        File.Move(sourceFilePath, archiveFilePath);
                        Log.Information(string.Format("Moved [{0}] to [{1}] for archiving purpose.", sourceFilePath, archiveFilePath));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(string.Format("Unable to Archive Source File [{0}] to [{1}]: {2}. Skipping Archival...Please perform archiving manually.", sourceFilePath, archiveFilePath, ex.Message));
                    }
                }
            }


        }

        /// <summary>
        /// Decrypt File and Verify the Signature
        /// </summary>
        /// <param name="sourceFilePath"></param>
        /// <param name="destinationFilePath"></param>
        /// <param name="archiveFilePath"></param>
        public void DecryptFileAndVerifySignature(string sourceFilePath, string destinationFilePath, string? archiveFilePath)
        {
            // Validate initialization
            if (_ctx == null)
                throw new InvalidOperationException("OpenPgpHelper not initialized. Call Init() first.");

            GpgmeData cipher = new GpgmeFileData(sourceFilePath);
            GpgmeData plain = new GpgmeFileData(destinationFilePath);

            /////// DECRYPT AND VERIFY DATA ///////
            _ctx.ClearPassphraseFunction();
            _ctx.SetPassphraseFunction(RecipientPassphraseCallback);
            _ctx.PinentryMode = PinentryMode.Loopback; // Use the Loopback option to supply the secretPassphrase programmatically

            Log.Information($"Source File Size: {cipher.Length / 1024.0:N2} KBytes...");

            CombinedResult comrst = _ctx.DecryptAndVerify(
                cipher, // source buffer
                plain); // destination buffer

            Log.Information($"Destination File Size: {plain.Length / 1024.0:N2} KBytes...");

            plain.Close();
            cipher.Close();

            /* print out all recipients key ids (a PGP package can be 
             * encrypted to various recipients).
             */
            DecryptionResult decrst = comrst.DecryptionResult;


            if (decrst.Recipients != null)
                foreach (Recipient recp in decrst.Recipients)
                {
                    Log.Information(string.Format("File Decryption: Key id {0} with {1} algorithm",
                        recp.KeyId,
                        Gpgme.GetPubkeyAlgoName(recp.KeyAlgorithm)));
                }
            else
                Log.Warning("Recipients None!");

            int countMatchingSenderThumbprint = 0;
            // print out signature information
            VerificationResult verrst = comrst.VerificationResult;
            if (verrst.Signature != null)
            {
                foreach (Libgpgme.Signature sig in verrst.Signature)
                {
                    Log.Information(string.Format("Sender's Signatures Verifications: "
                        + "\n\tFingerprint: {0}"
                        + "\n\tHash algorithm: {1}"
                        + "\n\tKey algorithm: {2}"
                        + "\n\tTimestamp: {3}"
                        + "\n\tSummary: {4}"
                        + "\n\tValidity: {5}",
                        sig.Fingerprint,
                        Gpgme.GetHashAlgoName(sig.HashAlgorithm),
                        Gpgme.GetPubkeyAlgoName(sig.PubkeyAlgorithm),
                        sig.Timestamp,
                        sig.Summary,
                        sig.Validity));

                    if (sig.Summary.ToString().ToLower().Contains("valid") && sig.Validity.ToString().ToLower().Contains("full"))
                    {
                        // Sender's Signature is valid
                        if (VerifyKeyThumbprints(SenderKey, sig.Fingerprint, "Verifying whether the fingerprint of the actual Sender's key-in-use matches that of the configured Sender's key-for-use."))
                        {
                            countMatchingSenderThumbprint++;
                        }
                    }
                    else
                    {
                        // Sender's Signature is invalid
                        Log.Error($"Sender's Signature with Fingerprint [{sig.Fingerprint}] is invalid!");
                    }
                }
            }

            if (!string.IsNullOrEmpty(archiveFilePath))
            {
                try
                {
                    File.Move(sourceFilePath, archiveFilePath);
                    Log.Information(string.Format("Moved [{0}] to [{1}] for archiving purpose.", sourceFilePath, archiveFilePath));
                }
                catch (Exception ex)
                {
                    Log.Warning(string.Format("Unable to Archive Source File [{0}] to [{1}]: {2}. Skipping Archival...Please perform archiving manually.", sourceFilePath, archiveFilePath, ex.Message));
                }
            }

            if (countMatchingSenderThumbprint == 0) // Failed Sender Authentication!
            {
                File.Delete(destinationFilePath); // Delete the destination file due to failed sender authentication for security reason
                throw new Exception($"Sender Authentication Failed! Either the Sender's Signature is invalid or the fingerprint of the actual Sender's key-in-use does not match with the configured Sender's key-for-use! The decrypted file [{destinationFilePath}] has been deleted due to failed sender authentication for security reason.");
            }

            Log.Information(string.Format("Successfully Decrypted and Verified [{0}] and saved it to [{1}]!", sourceFilePath, destinationFilePath));
        }

        /// <summary>
        /// Function to test the configured Secrets Manager
        /// </summary>
        public void TestSecretsManager()
        {
            Log.Information("Testing Secrets Manager...");
            string senderSecretPassphrase = GetSenderSecretPassphrase();
            string recipientSecretPassphrase = GetRecipientSecretPassphrase();
        }

        /// <summary>
        /// Helper function to retrieve the Sender's Secret Passphrase from the configured Secrets Manager
        /// </summary>
        /// <returns>The Secret Passphrase</returns>
        private string GetSenderSecretPassphrase()
        {

            string passphraseProtectionMode = RuntimeAppSettings["PassphraseProtectionMode"].Trim().ToUpper();
            string senderSecretPassphrase;

            Log.Verbose(string.Format("Fetching Sender's Secret Passphrase programmatically [{0}]...", passphraseProtectionMode));

            switch (passphraseProtectionMode)
            {
                case "AWS_SECRETSMANAGER":
                    {
                        // Recommended for AWS serverless and containerized based solutions. Also useful for Applications hosted on AWS EC2 Instances.
                        GetSecretsFromAWSSecretsManager sm = new();
                        string senderSecretPassphraseID = RuntimeAppSettings["SenderAWSSecretsName"]; //Set the Secret Name configured in AWS Secrets Manager
                        var retrievedSecrets = JsonSerializer.Deserialize<Dictionary<string, string>>(GetSecretsFromAWSSecretsManager.GetSecretString(senderSecretPassphraseID));
                        if (retrievedSecrets == null || !retrievedSecrets.TryGetValue("SecretPassPhrase", out var secretValue))
                            throw new InvalidOperationException("Failed to retrieve SecretPassPhrase from AWS Secrets Manager");
                        senderSecretPassphrase = secretValue;
                        Log.Verbose("Fetched Secret Passphrase from AWS Secrets Manager...");
                    }
                    break;
                case "WINDOWS_DPAPI":
                    {
                        // Only works for Systems developed for Windows OS. Optimized for Windows-Based Applications! 
                        string senderSecretPassphraseEncrypted = RuntimeAppSettings["SenderEncryptedSecretPassPhrase_WIND_DPAPI"];
                        string entropy = RuntimeAppSettings["entropy"];
                        senderSecretPassphrase = DecryptSecretsFromAppConfigWithWindowsDataProtectionAPI.GetSecretString(senderSecretPassphraseEncrypted, entropy);

                        Log.Verbose("Decrypted Secret Passphrase using Windows Data Protection API...");
                    }
                    break;
                case "ASPNET_DPAPI":
                default:
                    {
                        // Works for Windows, Linux and macOS based Applications. Can be used on any .NET core applications, including non-ASP.NET ones. Recommended for all other types of Applications that cannot use Solutions 1 & 2. 
                        string senderSecretPassphraseEncrypted = RuntimeAppSettings["SenderEncryptedSecretPassPhrase_ASP_DPAPI"];
                        string entropy = RuntimeAppSettings["entropy"];
                        string sslCertDistinguishedSubjectName = RuntimeAppSettings["SSLCertDistinguishedSubjectName"];
                        senderSecretPassphrase = DecryptSecretsFromAppConfigWithASPNETCoreDPAPI.GetSecretString(senderSecretPassphraseEncrypted, entropy, sslCertDistinguishedSubjectName);
                        Log.Verbose("Decrypted Secret Passphrase using ASP.NET Core Data Protection API...");
                    }
                    break;
            }

            return senderSecretPassphrase;
        }

        /// <summary>
        /// Helper function to retrieve the Recipient's Secret Passphrase from the configured Secrets Manager
        /// </summary>
        /// <returns></returns>
        private string GetRecipientSecretPassphrase()
        {

            string passphraseProtectionMode = RuntimeAppSettings["PassphraseProtectionMode"].Trim().ToUpper();
            string recipientSecretPassphrase;
            Log.Verbose(string.Format("Fetching Recipient's Secret Passphrase programmatically [{0}]...", passphraseProtectionMode));
            switch (passphraseProtectionMode)
            {
                case "AWS_SECRETSMANAGER":
                    {
                        // Recommended for AWS serverless and containerized based solutions. Also useful for Applications hosted on AWS EC2 Instances.
                        GetSecretsFromAWSSecretsManager sm = new();
                        string recipientSecretPassphraseID = RuntimeAppSettings["RecipientAWSSecretsName"]; //Set the Secret Name configured in AWS Secrets Manager
                        var retrievedSecrets = JsonSerializer.Deserialize<Dictionary<string, string>>(GetSecretsFromAWSSecretsManager.GetSecretString(recipientSecretPassphraseID));
                        if (retrievedSecrets == null || !retrievedSecrets.TryGetValue("SecretPassPhrase", out var secretValue))
                            throw new InvalidOperationException("Failed to retrieve SecretPassPhrase from AWS Secrets Manager");
                        recipientSecretPassphrase = secretValue;
                        Log.Verbose("Fetched Secret Passphrase from AWS Secrets Manager...");
                    }
                    break;
                case "WINDOWS_DPAPI":
                    {
                        // Only works for Systems developed for Windows OS. Optimized for Windows-Based Applications! 
                        string recipientSecretPassphraseEncrypted = RuntimeAppSettings["RecipientEncryptedSecretPassPhrase_WIND_DPAPI"];
                        string entropy = RuntimeAppSettings["entropy"];
                        recipientSecretPassphrase = DecryptSecretsFromAppConfigWithWindowsDataProtectionAPI.GetSecretString(recipientSecretPassphraseEncrypted, entropy);
                        Log.Verbose("Decrypted Secret Passphrase using Windows Data Protection API...");
                    }
                    break;
                case "ASPNET_DPAPI":
                default:
                    {
                        // Works for Windows, Linux and macOS based Applications. Can be used on any .NET core applications, including non-ASP.NET ones. Recommended for all other types of Applications that cannot use Solutions 1 & 2. 
                        string recipientSecretPassphraseEncrypted = RuntimeAppSettings["RecipientEncryptedSecretPassPhrase_ASP_DPAPI"];
                        string entropy = RuntimeAppSettings["entropy"];
                        string sslCertDistinguishedSubjectName = RuntimeAppSettings["SSLCertDistinguishedSubjectName"];
                        recipientSecretPassphrase = DecryptSecretsFromAppConfigWithASPNETCoreDPAPI.GetSecretString(recipientSecretPassphraseEncrypted, entropy, sslCertDistinguishedSubjectName);
                        Log.Verbose("Decrypted Secret Passphrase using ASP.NET Core Data Protection API...");
                    }
                    break;
            }

            return recipientSecretPassphrase;
        }
        /// <summary>
        /// Sender's Passphrase callback method. Invoked if a action requires the user's password.
        /// </summary>
        /// <param name="ctx">Context that has invoked the callback.</param>
        /// <param name="info">Information about the key.</param>
        /// <param name="passwd">User supplied password.</param>
        /// <returns></returns>
        public PassphraseResult SenderPassphraseCallback(
           Context ctx,
           PassphraseInfo info,
           ref char[] passwd)
        {
            string senderSecretPassphrase = GetSenderSecretPassphrase();

            passwd = senderSecretPassphrase.ToCharArray();
            return PassphraseResult.Success;
        }

        /// <summary>
        /// Recipient's Passphrase callback method. Invoked if a action requires the user's password.
        /// </summary>
        /// <param name="ctx">Context that has invoked the callback.</param>
        /// <param name="info">Information about the key.</param>
        /// <param name="passwd">User supplied password.</param>
        /// <returns></returns>
        public PassphraseResult RecipientPassphraseCallback(
               Context ctx,
               PassphraseInfo info,
               ref char[] passwd)
        {
            string recipientSecretPassphrase = GetRecipientSecretPassphrase();
            passwd = recipientSecretPassphrase.ToCharArray();
            return PassphraseResult.Success;
        }


        /// <summary>
        /// The Purpose of this function is to verify the thumbprint of the actual key-in-use matches that of the configured key-for-use.
        /// </summary>
        /// <param name="keyForUse"></param>
        /// <param name="thumbprintOfKeyInUse"></param>
        /// <param name="useCase"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public bool VerifyKeyThumbprints(Libgpgme.PgpKey keyForUse, string thumbprintOfKeyInUse, string useCase = "Verifying the thumbprint of the actual key-in-use matches that of the configured key-for-use.")
        {
            Log.Information(useCase);

            if (keyForUse == null)
            {
                throw new Exception("Supplied Key-for-use is null!");
            }

            if (thumbprintOfKeyInUse == null)
            {
                throw new Exception("Supplied Thumbprint of Key-in-use is null!");
            }


            var subkeysThumbprints = new List<string>
            {
                keyForUse.Subkeys.Fingerprint
            };
            if (keyForUse.Subkeys.Next != null) 
            { 
                subkeysThumbprints.Add(keyForUse.Subkeys.Next.Fingerprint); 
            }

            bool ismatching = subkeysThumbprints.Contains(thumbprintOfKeyInUse);

            Log.Information($"Key-in-Use matches Key-for-Use?...[{ismatching}]");

            return ismatching;

        }

        /// <summary>
        /// Prints the properties of the GPG Engine Info that is installed on the server
        /// </summary>
        /// <param name="engineInfo"></param>
        /// <returns></returns>
        private string PrintEngineInfoProperties(Libgpgme.EngineInfo engineInfo)
        {
            // Printing the EngineInfo Properties
            var engineInfoPropertiesToPrint = new List<string> { "FileName", "Protocol", "ReqVersion", "Version"};
            StringBuilder sb = new();
            sb.Append("Engine Info Properties: ");
            sb.AppendLine(ObjectPrinter.PrintProperties(engineInfo, engineInfoPropertiesToPrint).ToString());

            var ei = engineInfo;
            sb.Append("Engine Info Properties: ");

            while (ei != null)
            {
                sb.AppendLine(ObjectPrinter.PrintProperties(ei, engineInfoPropertiesToPrint).ToString());

                ei = ei.Next;
            }
            
            return sb.ToString();
        }
    }
}

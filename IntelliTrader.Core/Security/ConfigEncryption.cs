using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace IntelliTrader.Core.Security
{
    /// <summary>
    /// Provides AES-256-CBC encryption and decryption for configuration files.
    /// Uses PBKDF2 with SHA256 for key derivation from a master password.
    /// Encrypted content is prefixed with "ENC:" followed by base64-encoded data.
    /// The binary layout is: [16-byte salt][16-byte IV][ciphertext].
    /// </summary>
    public static class ConfigEncryption
    {
        private const string EncryptedPrefix = "ENC:";
        private const int KeySizeBytes = 32;   // AES-256
        private const int SaltSizeBytes = 16;
        private const int IvSizeBytes = 16;    // AES block size
        private const int Pbkdf2Iterations = 100_000;

        /// <summary>
        /// Checks whether the given content is an encrypted config blob.
        /// </summary>
        public static bool IsEncrypted(string content)
        {
            return !string.IsNullOrEmpty(content) &&
                   content.StartsWith(EncryptedPrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Encrypts plaintext JSON using AES-256-CBC with a key derived from
        /// <paramref name="masterPassword"/> via PBKDF2 (100 000 iterations, SHA256).
        /// Returns a string in the form "ENC:&lt;base64&gt;".
        /// </summary>
        public static string Encrypt(string plaintext, string masterPassword)
        {
            if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
            if (string.IsNullOrEmpty(masterPassword)) throw new ArgumentException("Master password must not be empty.", nameof(masterPassword));

            byte[] salt = new byte[SaltSizeBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            byte[] key = DeriveKey(masterPassword, salt);

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.GenerateIV();

                byte[] iv = aes.IV;

                using (var encryptor = aes.CreateEncryptor())
                {
                    byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
                    byte[] ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

                    // Layout: salt + IV + ciphertext
                    byte[] blob = new byte[SaltSizeBytes + IvSizeBytes + ciphertext.Length];
                    Buffer.BlockCopy(salt, 0, blob, 0, SaltSizeBytes);
                    Buffer.BlockCopy(iv, 0, blob, SaltSizeBytes, IvSizeBytes);
                    Buffer.BlockCopy(ciphertext, 0, blob, SaltSizeBytes + IvSizeBytes, ciphertext.Length);

                    return EncryptedPrefix + Convert.ToBase64String(blob);
                }
            }
        }

        /// <summary>
        /// Decrypts a string previously produced by <see cref="Encrypt"/>.
        /// The input must start with the "ENC:" prefix.
        /// </summary>
        public static string Decrypt(string ciphertext, string masterPassword)
        {
            if (ciphertext == null) throw new ArgumentNullException(nameof(ciphertext));
            if (string.IsNullOrEmpty(masterPassword)) throw new ArgumentException("Master password must not be empty.", nameof(masterPassword));

            if (!IsEncrypted(ciphertext))
            {
                throw new ArgumentException("Content does not appear to be encrypted (missing ENC: prefix).", nameof(ciphertext));
            }

            string base64 = ciphertext.Substring(EncryptedPrefix.Length);
            byte[] blob = Convert.FromBase64String(base64);

            if (blob.Length < SaltSizeBytes + IvSizeBytes + 1)
            {
                throw new CryptographicException("Encrypted blob is too short to be valid.");
            }

            byte[] salt = new byte[SaltSizeBytes];
            byte[] iv = new byte[IvSizeBytes];
            byte[] encrypted = new byte[blob.Length - SaltSizeBytes - IvSizeBytes];

            Buffer.BlockCopy(blob, 0, salt, 0, SaltSizeBytes);
            Buffer.BlockCopy(blob, SaltSizeBytes, iv, 0, IvSizeBytes);
            Buffer.BlockCopy(blob, SaltSizeBytes + IvSizeBytes, encrypted, 0, encrypted.Length);

            byte[] key = DeriveKey(masterPassword, salt);

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = key;
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                {
                    byte[] plaintextBytes = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                    return Encoding.UTF8.GetString(plaintextBytes);
                }
            }
        }

        private static byte[] DeriveKey(string password, byte[] salt)
        {
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(KeySizeBytes);
            }
        }
    }
}

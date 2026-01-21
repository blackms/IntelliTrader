using IntelliTrader.Core;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IntelliTrader.Web
{
    /// <summary>
    /// Implements secure password hashing using BCrypt with backward compatibility for legacy MD5 hashes.
    ///
    /// SECURITY NOTES:
    /// - New passwords should ALWAYS use BCrypt (HashPassword method)
    /// - MD5 support is ONLY for verifying existing legacy passwords during migration
    /// - Work factor of 12 provides good security vs performance trade-off (approximately 250ms on modern hardware)
    ///
    /// MIGRATION PATH:
    /// 1. Update stored MD5 hashes to BCrypt hashes using the HashPassword method
    /// 2. Use IsLegacyHash to identify passwords that need migration
    /// 3. After verifying a legacy hash, re-hash with BCrypt and update storage
    /// </summary>
    public class PasswordService : IPasswordService
    {
        private const int DefaultWorkFactor = 12;

        /// <inheritdoc />
        public string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentNullException(nameof(password), "Password cannot be null or empty");
            }

            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: DefaultWorkFactor);
        }

        /// <inheritdoc />
        public bool VerifyPassword(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            // Check if it's a BCrypt hash (starts with $2a$ or $2b$)
            if (IsBCryptHash(storedHash))
            {
                try
                {
                    return BCrypt.Net.BCrypt.Verify(password, storedHash);
                }
                catch (BCrypt.Net.SaltParseException)
                {
                    // Invalid BCrypt hash format
                    return false;
                }
            }

            // Legacy: MD5 hash (32 hex characters)
            if (IsLegacyHash(storedHash))
            {
                var md5Hash = ComputeMD5Hash(password);
                return md5Hash.Equals(storedHash, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <inheritdoc />
        public bool IsLegacyHash(string storedHash)
        {
            // MD5 produces a 32-character hex string
            return !string.IsNullOrEmpty(storedHash) &&
                   storedHash.Length == 32 &&
                   storedHash.All(c => char.IsLetterOrDigit(c)) &&
                   !storedHash.StartsWith("$");
        }

        /// <inheritdoc />
        public bool IsBCryptHash(string storedHash)
        {
            return !string.IsNullOrEmpty(storedHash) &&
                   (storedHash.StartsWith("$2a$") || storedHash.StartsWith("$2b$") || storedHash.StartsWith("$2y$"));
        }

        /// <summary>
        /// Computes MD5 hash for legacy password verification.
        /// DO NOT use for new passwords - use HashPassword() instead.
        /// </summary>
        [Obsolete("Use HashPassword() for new passwords. This is only for verifying legacy MD5 hashes.")]
        private static string ComputeMD5Hash(string input)
        {
            using (var md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.ASCII.GetBytes(input);
                byte[] hash = md5.ComputeHash(inputBytes);

                var sb = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("X2"));
                }
                return sb.ToString();
            }
        }
    }
}

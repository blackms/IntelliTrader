namespace IntelliTrader.Core
{
    /// <summary>
    /// Service for secure password hashing and verification.
    /// Supports BCrypt hashing with automatic detection of legacy MD5 hashes for backward compatibility.
    /// </summary>
    public interface IPasswordService
    {
        /// <summary>
        /// Hashes a password using BCrypt with the default work factor.
        /// </summary>
        /// <param name="password">The plaintext password to hash.</param>
        /// <returns>A BCrypt hash string (starts with $2a$ or $2b$).</returns>
        string HashPassword(string password);

        /// <summary>
        /// Verifies a password against a stored hash.
        /// Automatically detects whether the hash is BCrypt or legacy MD5.
        /// </summary>
        /// <param name="password">The plaintext password to verify.</param>
        /// <param name="storedHash">The stored hash (BCrypt or legacy MD5).</param>
        /// <returns>True if the password matches the hash; otherwise, false.</returns>
        bool VerifyPassword(string password, string storedHash);

        /// <summary>
        /// Determines whether the stored hash is using a legacy (insecure) algorithm.
        /// </summary>
        /// <param name="storedHash">The stored hash to check.</param>
        /// <returns>True if the hash is using a legacy algorithm (MD5); otherwise, false.</returns>
        bool IsLegacyHash(string storedHash);

        /// <summary>
        /// Determines whether the stored hash is using BCrypt.
        /// </summary>
        /// <param name="storedHash">The stored hash to check.</param>
        /// <returns>True if the hash is using BCrypt; otherwise, false.</returns>
        bool IsBCryptHash(string storedHash);
    }
}

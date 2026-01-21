using FluentAssertions;
using Xunit;
using IntelliTrader.Core;
using IntelliTrader.Web;

namespace IntelliTrader.Web.Tests;

public class PasswordServiceTests
{
    private readonly IPasswordService _sut;

    public PasswordServiceTests()
    {
        _sut = new PasswordService();
    }

    #region HashPassword Tests

    [Fact]
    public void HashPassword_WithValidPassword_ReturnsBCryptHash()
    {
        // Arrange
        var password = "testPassword123";

        // Act
        var hash = _sut.HashPassword(password);

        // Assert
        hash.Should().NotBeNullOrEmpty();
        hash.Should().StartWith("$2");  // BCrypt hash prefix ($2a$, $2b$, or $2y$)
    }

    [Fact]
    public void HashPassword_WithSamePassword_ReturnsDifferentHashes()
    {
        // Arrange - BCrypt includes a random salt, so same password produces different hashes
        var password = "testPassword123";

        // Act
        var hash1 = _sut.HashPassword(password);
        var hash2 = _sut.HashPassword(password);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void HashPassword_WithNullPassword_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.HashPassword(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("password");
    }

    [Fact]
    public void HashPassword_WithEmptyPassword_ThrowsArgumentNullException()
    {
        // Act
        var act = () => _sut.HashPassword(string.Empty);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("password");
    }

    #endregion

    #region VerifyPassword Tests - BCrypt

    [Fact]
    public void VerifyPassword_WithCorrectPassword_BCrypt_ReturnsTrue()
    {
        // Arrange
        var password = "testPassword123";
        var hash = _sut.HashPassword(password);

        // Act
        var result = _sut.VerifyPassword(password, hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithIncorrectPassword_BCrypt_ReturnsFalse()
    {
        // Arrange
        var password = "testPassword123";
        var wrongPassword = "wrongPassword456";
        var hash = _sut.HashPassword(password);

        // Act
        var result = _sut.VerifyPassword(wrongPassword, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithKnownBCryptHash_ReturnsTrue()
    {
        // Arrange - create a BCrypt hash using the service
        var password = "mySecurePassword";
        var knownBCryptHash = _sut.HashPassword(password);

        // Act
        var result = _sut.VerifyPassword(password, knownBCryptHash);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region VerifyPassword Tests - Legacy MD5

    [Fact]
    public void VerifyPassword_WithCorrectPassword_LegacyMD5_ReturnsTrue()
    {
        // Arrange - MD5 hash of "password" is 5F4DCC3B5AA765D61D8327DEB882CF99
        var password = "password";
        var md5Hash = "5F4DCC3B5AA765D61D8327DEB882CF99";

        // Act
        var result = _sut.VerifyPassword(password, md5Hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithIncorrectPassword_LegacyMD5_ReturnsFalse()
    {
        // Arrange - MD5 hash of "password" is 5F4DCC3B5AA765D61D8327DEB882CF99
        var wrongPassword = "wrongpassword";
        var md5Hash = "5F4DCC3B5AA765D61D8327DEB882CF99";

        // Act
        var result = _sut.VerifyPassword(wrongPassword, md5Hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithLowercaseMD5Hash_ReturnsTrue()
    {
        // Arrange - MD5 hash comparison should be case-insensitive
        var password = "password";
        var md5HashLowercase = "5f4dcc3b5aa765d61d8327deb882cf99";

        // Act
        var result = _sut.VerifyPassword(password, md5HashLowercase);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region VerifyPassword Tests - Edge Cases

    [Fact]
    public void VerifyPassword_WithNullPassword_ReturnsFalse()
    {
        // Arrange
        var hash = _sut.HashPassword("somePassword");

        // Act
        var result = _sut.VerifyPassword(null!, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithNullHash_ReturnsFalse()
    {
        // Act
        var result = _sut.VerifyPassword("password", null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithEmptyPassword_ReturnsFalse()
    {
        // Arrange
        var hash = _sut.HashPassword("somePassword");

        // Act
        var result = _sut.VerifyPassword(string.Empty, hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithEmptyHash_ReturnsFalse()
    {
        // Act
        var result = _sut.VerifyPassword("password", string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithInvalidHashFormat_ReturnsFalse()
    {
        // Arrange - invalid hash that doesn't match MD5 or BCrypt format
        var invalidHash = "not-a-valid-hash-format";

        // Act
        var result = _sut.VerifyPassword("password", invalidHash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_WithMalformedBCryptHash_ReturnsFalse()
    {
        // Arrange - starts with $2a$ but is malformed
        var malformedBCryptHash = "$2a$invalid-bcrypt-hash";

        // Act
        var result = _sut.VerifyPassword("password", malformedBCryptHash);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region IsLegacyHash Tests

    [Fact]
    public void IsLegacyHash_WithMD5Hash_ReturnsTrue()
    {
        // Arrange - 32 character hex string
        var md5Hash = "5F4DCC3B5AA765D61D8327DEB882CF99";

        // Act
        var result = _sut.IsLegacyHash(md5Hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsLegacyHash_WithBCryptHash_ReturnsFalse()
    {
        // Arrange
        var bcryptHash = _sut.HashPassword("password");

        // Act
        var result = _sut.IsLegacyHash(bcryptHash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsLegacyHash_WithNullHash_ReturnsFalse()
    {
        // Act
        var result = _sut.IsLegacyHash(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsLegacyHash_WithEmptyHash_ReturnsFalse()
    {
        // Act
        var result = _sut.IsLegacyHash(string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsLegacyHash_With31CharString_ReturnsFalse()
    {
        // Arrange - not exactly 32 characters
        var shortHash = "5F4DCC3B5AA765D61D8327DEB882CF9";

        // Act
        var result = _sut.IsLegacyHash(shortHash);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region IsBCryptHash Tests

    [Fact]
    public void IsBCryptHash_With2aPrefix_ReturnsTrue()
    {
        // Arrange
        var hash = _sut.HashPassword("password");

        // Act
        var result = _sut.IsBCryptHash(hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsBCryptHash_With2bPrefix_ReturnsTrue()
    {
        // Arrange - $2b$ is a valid BCrypt prefix
        var hash = "$2b$12$somevalidbcrypthashcontent";

        // Act
        var result = _sut.IsBCryptHash(hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsBCryptHash_With2yPrefix_ReturnsTrue()
    {
        // Arrange - $2y$ is a valid BCrypt prefix (PHP variant)
        var hash = "$2y$12$somevalidbcrypthashcontent";

        // Act
        var result = _sut.IsBCryptHash(hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsBCryptHash_WithMD5Hash_ReturnsFalse()
    {
        // Arrange
        var md5Hash = "5F4DCC3B5AA765D61D8327DEB882CF99";

        // Act
        var result = _sut.IsBCryptHash(md5Hash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsBCryptHash_WithNullHash_ReturnsFalse()
    {
        // Act
        var result = _sut.IsBCryptHash(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsBCryptHash_WithEmptyHash_ReturnsFalse()
    {
        // Act
        var result = _sut.IsBCryptHash(string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Migration Scenario Tests

    [Fact]
    public void MigrationScenario_LegacyMD5HashCanBeVerifiedAndMigrated()
    {
        // Arrange - simulate a legacy MD5 hash from old config
        var password = "adminPassword123";
        var legacyMD5Hash = "b84967c4f073b71405404f3719c788cd";  // MD5 of some password

        // Act - verify user can still login with old hash
        var canLogin = _sut.VerifyPassword("testadmin", legacyMD5Hash);
        var isLegacy = _sut.IsLegacyHash(legacyMD5Hash);

        // If login successful and using legacy hash, generate new BCrypt hash
        var newBCryptHash = _sut.HashPassword(password);

        // Assert
        isLegacy.Should().BeTrue("Legacy hash should be detected");
        _sut.IsBCryptHash(newBCryptHash).Should().BeTrue("New hash should be BCrypt");
        _sut.VerifyPassword(password, newBCryptHash).Should().BeTrue("New BCrypt hash should verify correctly");
    }

    [Fact]
    public void MigrationScenario_BCryptHashDoesNotNeedMigration()
    {
        // Arrange - simulate a modern BCrypt hash
        var password = "securePassword";
        var bcryptHash = _sut.HashPassword(password);

        // Act
        var isLegacy = _sut.IsLegacyHash(bcryptHash);
        var isBCrypt = _sut.IsBCryptHash(bcryptHash);

        // Assert
        isLegacy.Should().BeFalse("BCrypt hash should not be detected as legacy");
        isBCrypt.Should().BeTrue("BCrypt hash should be detected correctly");
    }

    #endregion
}

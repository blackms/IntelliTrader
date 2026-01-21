using FluentValidation.Results;

namespace IntelliTrader.Core.Validation;

/// <summary>
/// Service for validating configuration objects.
/// </summary>
public interface IConfigValidationService
{
    /// <summary>
    /// Validates a configuration object and throws ConfigValidationException if invalid.
    /// </summary>
    /// <typeparam name="T">The configuration type to validate.</typeparam>
    /// <param name="config">The configuration object to validate.</param>
    /// <param name="sectionName">The configuration section name (for error messages).</param>
    /// <exception cref="ConfigValidationException">Thrown when validation fails.</exception>
    void ValidateAndThrow<T>(T config, string sectionName) where T : class;

    /// <summary>
    /// Validates a configuration object and returns the result without throwing.
    /// </summary>
    /// <typeparam name="T">The configuration type to validate.</typeparam>
    /// <param name="config">The configuration object to validate.</param>
    /// <returns>Validation result containing any errors.</returns>
    ValidationResult Validate<T>(T config) where T : class;

    /// <summary>
    /// Checks if a configuration object is valid.
    /// </summary>
    /// <typeparam name="T">The configuration type to validate.</typeparam>
    /// <param name="config">The configuration object to validate.</param>
    /// <returns>True if valid, false otherwise.</returns>
    bool IsValid<T>(T config) where T : class;
}
